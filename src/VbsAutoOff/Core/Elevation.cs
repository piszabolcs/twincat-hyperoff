using System.Security.Principal;
namespace VbsAutoOff.Core
{
    public static class Elevation
    {
        public static bool IsAdmin()
        {
            using (var id = WindowsIdentity.GetCurrent())
            {
                var p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }
}
