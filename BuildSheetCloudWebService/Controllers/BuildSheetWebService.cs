using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ApiTest.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BuildSheetWebService(IConfiguration configuration) : ControllerBase
    {

        private readonly IConfiguration _configuration = configuration;

        #region Getter
        [HttpGet("execute-query")]
        public async Task<IActionResult> ExecuteQuery([FromQuery] string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return BadRequest(new { message = "Query is required" });
            }

            // Set response headers to prevent caching
            Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            Response.Headers.Append("Pragma", "no-cache");
            Response.Headers.Append("Expires", "0");

            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    // Extract the column name from the input query
                    var selectIndex = query.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
                    var fromIndex = query.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);

                    var columnName = query.Substring(selectIndex + 6, fromIndex - (selectIndex + 6)).Trim();

                    // Extract the WHERE clause from the input query
                    var whereIndex = query.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);

                    if (selectIndex == -1 || fromIndex <= selectIndex)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = "Invalid query format. Please include a valid SELECT clause." });
                    }
                    else if (fromIndex == -1 || fromIndex <= selectIndex)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = "Invalid query format. Please include a valid FROM clause." });
                    }
                    else if (whereIndex == -1 || whereIndex <= fromIndex)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = "Invalid query format. Please include a valid WHERE clause." });
                    }

                    var tabName = query.Substring(fromIndex + 4, whereIndex - (fromIndex + 4));
                    var whereClause = query.Substring(whereIndex);

                    var selectQuery = $"SELECT TOP (1) {columnName}, rowversion FROM {tabName} {whereClause}";

                    // Execute the SELECT query
                    using var selectCommand = new SqlCommand(selectQuery, connection, transaction);
                    using var reader = await selectCommand.ExecuteReaderAsync();

                    if (!reader.HasRows)
                    {
                        await transaction.RollbackAsync();
                        return NotFound(new { message = "No matching record found." });
                    }

                    await reader.ReadAsync();
                    var value = reader[columnName];
                    var rowVersion = Convert.ToBase64String((byte[])reader["rowversion"]);

                    // Close the reader before executing the update command
                    await reader.DisposeAsync();


                    await transaction.CommitAsync();
                    return Ok(new { value, rowVersion });
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                if (ex is SqlException sqlEx)
                {
                    // Check for network-related errors in the SqlException
                    if (sqlEx.Number == -1 || sqlEx.Number == 53)
                    {
                        return BadRequest(new { message = "Cannot connect to the server. Please check your internet connection or contact support.",details = sqlEx.Message });
                    }

                    // Other SQL exceptions
                    return BadRequest(new { message = "A database error occurred.", details = sqlEx.Message });
                }
                else if (ex is HttpRequestException httpEx)
                {
                    return BadRequest(new { message = "Network error while connecting to the cloud. Please check your internet connection.", details = httpEx.Message });
                }
                else if (ex is ApplicationException appEx)
                {
                    return BadRequest(new { message = appEx.Message, details = appEx.InnerException?.Message });
                }
                return StatusCode(500, new { message = ex.Message });
            }
        }
        #endregion

        #region Insert
        [HttpPost("insert-query")]
        public async Task<IActionResult> IntertQuery([FromQuery] string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return BadRequest(new { message = "Query is required." });
            }

            // Set response headers to prevent caching
            Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            Response.Headers.Append("Pragma", "no-cache");
            Response.Headers.Append("Expires", "0");

            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    using var updateCommand = new SqlCommand(query, connection, transaction);
                    var rowsAffected = await updateCommand.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        await transaction.CommitAsync();
                        return Ok(new { message = "Insertion Success" });
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = "Insert failed. Please redo." });
                    }
                }
                catch (SqlException ex) when (ex.Number == 2627) // Unique constraint violation
                {
                    await transaction.RollbackAsync();
                    return Conflict(new { message = "Duplicate entry detected." });
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                if (ex is SqlException sqlEx)
                {
                    // Check for network-related errors in the SqlException
                    if (sqlEx.Number == -1 || sqlEx.Number == 53)
                    {
                        return BadRequest(new { message = "Cannot connect to the server. Please check your internet connection or contact support.", details = sqlEx.Message });
                    }

                    // Other SQL exceptions
                    return BadRequest(new { message = "A database error occurred.", details = sqlEx.Message });
                }
                else if (ex is HttpRequestException httpEx)
                {
                    return BadRequest(new { message = "Network error while connecting to the cloud. Please check your internet connection.", details = httpEx.Message });
                }
                else if (ex is ApplicationException appEx)
                {
                    return BadRequest(new { message = appEx.Message, details = appEx.InnerException?.Message });
                }
                return StatusCode(500, new { message = ex.Message });
            }
        }
        #endregion

        #region Update
        [HttpPut("update-query")]
        public async Task<IActionResult> UpdateQuery([FromQuery] string query, [FromQuery] string rowVersion)
        {
            if (string.IsNullOrEmpty(query))
            {
                return BadRequest(new { message = "Query is required." });
            }

            if (string.IsNullOrEmpty(rowVersion))
            {
                return BadRequest(new { message = "Row version is required." });
            }

            // Set response headers to prevent caching
            Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            Response.Headers.Append("Pragma", "no-cache");
            Response.Headers.Append("Expires", "0");

            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    // Extract the SET clause and WHERE clause from the input query
                    var updateIndex = query.IndexOf("Update", StringComparison.OrdinalIgnoreCase);
                    var setIndex = query.IndexOf("SET", StringComparison.OrdinalIgnoreCase);
                    var whereIndex = query.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);

                    if (updateIndex == -1)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = "Invalid query format. Please include a valid update clause." });
                    }

                    else if (setIndex == -1 || setIndex <= updateIndex)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = "Invalid query format. Please include a valid SET clause." });
                    }
                    else if (whereIndex == -1 || whereIndex <= setIndex)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = "Invalid query format. Please include a valid WHERE clause." });
                    }

                    var tabName = query.Substring(updateIndex + 6, setIndex - (updateIndex + 6)).Trim();
                    var setClause = query.Substring(setIndex + 3, whereIndex - (setIndex + 3)).Trim();
                    var whereClause = query.Substring(whereIndex + 5).Trim();

                    var rowVersionBytes = Convert.FromBase64String(rowVersion);

                    // Execute the UPDATE query and set islock to false
                    var updateQuery = $@"
                        UPDATE {tabName}
                        SET {setClause}
                        WHERE {whereClause} AND RowVersion = @OldRowVersion";


                    using var updateCommand = new SqlCommand(updateQuery, connection, transaction);

                    updateCommand.Parameters.AddWithValue("@OldRowVersion", rowVersionBytes);

                    var rowsAffected = await updateCommand.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        await transaction.CommitAsync();
                        return Ok(new { message = setClause });
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = "Invalid row version. Please select again" });
                    }
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                if (ex is SqlException sqlEx)
                {
                    // Check for network-related errors in the SqlException
                    if (sqlEx.Number == -1 || sqlEx.Number == 53)
                    {
                        return BadRequest(new { message = "Cannot connect to the server. Please check your internet connection or contact support.", details = sqlEx.Message });
                    }

                    // Other SQL exceptions
                    return BadRequest(new { message = "A database error occurred.", details = sqlEx.Message });
                }
                else if (ex is HttpRequestException httpEx)
                {
                    return BadRequest(new { message = "Network error while connecting to the cloud. Please check your internet connection.", details = httpEx.Message });
                }
                else if (ex is ApplicationException appEx)
                {
                    return BadRequest(new { message = appEx.Message, details = appEx.InnerException?.Message });
                }
                return StatusCode(500, new { message = ex.Message });
            }
        }
        #endregion
    }
}


//[ApiController]
//[Route("api/[controller]")]
//public class BuildSheetWebService(ApplicationDbContext dbContext) : ControllerBase
//{
//private readonly ApplicationDbContext _dbContext = dbContext;

//[HttpPost("GetCounter")]
//public async Task<IActionResult> GetCounter([FromBody] CounterViewModel model)
//{
//    if (!ModelState.IsValid)
//        return BadRequest("Invalid model state");

//    try
//    {

//        // Get the DbSet dynamically based on the table name
//        var tableProperty = _dbContext.GetType().GetProperty(model.TableName);
//        if (tableProperty == null)
//            return BadRequest("Invalid table name.");

//        // Get the DbSet as IQueryable
//        var table = tableProperty.GetValue(_dbContext) as IQueryable<object>;
//        if (table == null)
//            return BadRequest("Failed to access the table.");

//        // Start building the query dynamically
//        var query = table.AsQueryable();

//        // Apply filters dynamically based on non-null model properties
//        foreach (var property in model.GetType().GetProperties())
//        {
//            var value = property.GetValue(model);
//            if (value == null || property.Name == "TableName")
//                continue;

//            // Build dynamic Where clause using Expression Trees
//            var parameter = Expression.Parameter(query.ElementType, "x");
//            var member = Expression.PropertyOrField(parameter, property.Name);
//            var constant = Expression.Constant(value);
//            var condition = Expression.Equal(member, constant);
//            var lambda = Expression.Lambda(condition, parameter);

//            var whereMethod = typeof(Queryable).GetMethods()
//                .First(m => m.Name == "Where" && m.GetParameters().Length == 2)
//                .MakeGenericMethod(query.ElementType);

//            query = (IQueryable<object>)whereMethod.Invoke(null, new object[] { query, lambda });
//        }

//        // Fetch the data
//        var data = await query.Cast<dynamic>().FirstOrDefaultAsync();

//        //var data = await _dbContext.TraceCodeUniverse
//        //    .Where(x => x.CustCode == model.CustCode && x.Device == model.Device && x.NumTraceCode == model.NumTraceCode)
//        //    .FirstOrDefaultAsync();

//        if (data == null)
//            return NotFound("Resource not found");

//        // Check if the data is already locked
//        if (data.IsLock == true)
//            return StatusCode(423, "Resource is already locked by another process.");

//        // Lock the data
//        data.IsLock = true;

//        _dbContext.Entry(data).State = EntityState.Modified;
//        await _dbContext.SaveChangesAsync();

//        return Ok();
//    }
//    catch (Exception ex)
//    {
//        return StatusCode(500, ex.Message);
//    }
//}

//[HttpPost("UpdateCounter")]
//public async Task<IActionResult> UpdateCounter([FromBody] CounterViewModel model)
//{
//    if (!ModelState.IsValid)
//        return BadRequest(ModelState);



//    try
//    {
//        //var data = await _dbContext.TraceCodeUniverse
//        //    .Where(x => x.CustCode == model.CustCode && x.Device == model.Device && x.NumTraceCode == model.NumTraceCode)
//        //    .FirstOrDefaultAsync();

//        // Get the DbSet dynamically based on the table name
//        var tableProperty = _dbContext.GetType().GetProperty(model.TableName);
//        if (tableProperty == null)
//            return BadRequest("Invalid table name.");

//        // Get the DbSet as IQueryable
//        var table = tableProperty.GetValue(_dbContext) as IQueryable<object>;
//        if (table == null)
//            return BadRequest("Failed to access the table.");

//        // Start building the query dynamically
//        var query = table.AsQueryable();

//        // Apply filters dynamically based on non-null model properties
//        foreach (var property in model.GetType().GetProperties())
//        {
//            var value = property.GetValue(model);
//            if (value == null || property.Name == "TableName" || property.Name == "Counter")
//                continue;

//            // Build dynamic Where clause using Expression Trees
//            var parameter = Expression.Parameter(query.ElementType, "x");
//            var member = Expression.PropertyOrField(parameter, property.Name);
//            var constant = Expression.Constant(value);
//            var condition = Expression.Equal(member, constant);
//            var lambda = Expression.Lambda(condition, parameter);

//            var whereMethod = typeof(Queryable).GetMethods()
//                .First(m => m.Name == "Where" && m.GetParameters().Length == 2)
//                .MakeGenericMethod(query.ElementType);

//            query = (IQueryable<object>)whereMethod.Invoke(null, new object[] { query, lambda });
//        }

//        // Fetch the data
//        var data = await query.Cast<dynamic>().FirstOrDefaultAsync();

//        if (data == null)
//            return NotFound();

//        if (!data.IsLock)
//            return StatusCode(423, "Resource is already unlock");

//        data.IsLock = false;
//        _dbContext.Entry(data).State = EntityState.Modified;
//        await _dbContext.SaveChangesAsync();

//        return Ok();
//    }
//    catch (Exception ex)
//    {
//        return StatusCode(500, ex.Message);
//    }
//}

