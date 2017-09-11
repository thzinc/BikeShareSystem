using System;
using System.Linq;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace BikeShareSystem
{
    class Program
    {
        static void Main(string[] args)
        {
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Console.Error.WriteLine($"Unhandled exception: {e.Exception.Message}");
                Console.Error.WriteLine(e.Exception.StackTrace);
                e.SetObserved();
            };

            var settingsPath = args.FirstOrDefault();

            using (var runner = new Runner(settingsPath))
            {
                Console.Write("Starting BikeShareSystem.Core...");
                runner.Start();
                AssemblyLoadContext.Default.Unloading += _ => runner.Stop();
                Console.WriteLine(" started!");

                runner.Wait();

                Console.Write("Stopping BikeShareSystem.Core...");
            }

            Console.WriteLine(" stopped!");
        }
    }
}
