using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CalendarSPA.Models
{
    public class Calendar
    {
        public int ID { get; set; }
        public string Description { get; set; }
        public string Owner { get; set; }
    }

    public class UserProfile
    {
        public string DisplayName { get; set; }
        public string GivenName { get; set; }
        public string Surname { get; set; }
    }
}
