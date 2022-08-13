using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Nop.Plugin.Misc.CoffeeApp.Models
{
    public class ListSquareConnect
    {
        public int Id { get; set; }
        public string CompanyName { get; set; }
        public string CafeName { get; set; }
        public bool UseSandbox { get; set; }
        public string Location { get; set; }
    }
}
