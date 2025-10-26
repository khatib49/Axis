using Hangfire.Dashboard;

namespace AxisAPI.Utils
{
    public class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            return true; 
        }
    }
}
