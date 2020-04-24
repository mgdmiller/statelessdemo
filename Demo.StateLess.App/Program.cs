using System;
using System.Threading.Tasks;

namespace Demo.StateLess.App
{
    class Program
    {
        
        static async Task Main(string[] args)
        {
            var file = new FileInformation();
            var machine = new FileMachine(file);
            
            await machine.Send("https://staticс.flexiway.ru/");
            
            Console.WriteLine("Press any key to exit");
            Console.ReadKey(true);
        }
    }
}