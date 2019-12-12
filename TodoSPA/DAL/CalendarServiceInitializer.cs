using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CalendarSPA.DAL
{
    class CalendarServiceInitializer : DropCreateDatabaseIfModelChanges<CalendarServiceContext>
    {
    }
}
