namespace CampariTest
{
    class Program
    {
        static void Main(string[] args)
        {
            TestGame game = new TestGame();
            
            var init = game.Initialize(1280, 720);

            if (init)
            {
                game.Run();
            }
            else
            {
                System.Console.WriteLine("uh oh!");
            }
        }
    }
}
