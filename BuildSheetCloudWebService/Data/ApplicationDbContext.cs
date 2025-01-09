//ApplicationDbContext
using Microsoft.EntityFrameworkCore;

namespace ApiTest.Server.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
    {

        //add DbSet property (table/collection in database) refers to model class of same name for columns
        //MUST sync to database after editing with:
        //tools -> nuget package manager -> console
        //add-migration exampleName
        //Update-Database


    }
}