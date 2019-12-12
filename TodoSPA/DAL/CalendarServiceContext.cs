using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CalendarListService;
using CalendarSPA.Models;

namespace CalendarSPA.DAL
{
    public class CalendarServiceContext : DbContext
    {
        public CalendarServiceContext()
            : base("CalendarServiceContext")
        { }
        public DbSet<Calendar> Calendares { get; set; }

        public DbSet<PerWebUserCache> PerUserCacheList { get; set; }
    }
}
