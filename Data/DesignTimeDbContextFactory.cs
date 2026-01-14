using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RetailMonolith.Data
{
    public class DesignTimeDbContextFactory: IDesignTimeDbContextFactory<AppDbContext>
    {
        /// <summary>
        ///   
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public AppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true");

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
