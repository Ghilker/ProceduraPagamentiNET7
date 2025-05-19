using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7.Moduli.Pagamenti.ProceduraPagamenti
{
    // A small container to track stats per impegno
    public class ImpegnoStats
    {
        // How many made it through final payment
        public int PaidCount { get; set; } = 0;

        // Accumulated paid amount for the final group
        public double TotalPaid { get; set; } = 0;

        // If you want to group the final distribution by "codEnte" or other categories
        // you can still keep your existing approach with a dictionary-of-dictionaries
        // or nest them here. For simplicity:
        public Dictionary<string, int> CategoryPaidCounts { get; set; } = new();

        // This tracks the number of students removed for various reasons
        // e.g.  "SENZA_IBAN" => 12, "NON_VINCITORE" => 5, etc.
        public Dictionary<string, int> RemovalReasons { get; set; } = new();

        // Utility method: increment a removal reason
        public void AddRemovalReason(string reason)
        {
            if (!RemovalReasons.ContainsKey(reason))
                RemovalReasons[reason] = 0;
            RemovalReasons[reason]++;
        }

        // Utility method: increment the (codEnte, count) category for final paid
        public void AddCategoryPaid(string category, int amount = 1)
        {
            if (!CategoryPaidCounts.ContainsKey(category))
                CategoryPaidCounts[category] = 0;
            CategoryPaidCounts[category] += amount;
        }
    }

}
