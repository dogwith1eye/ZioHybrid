using System;
using Unit = System.ValueTuple;
using static LaYumba.Functional.F;
using System.Threading;

namespace Zio
{
    interface ZIOApp<T> 
    {
        ZIO<T> Run();

        void Main(string[] args)
        {
            this.Run().Run(result => 
            {
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} The result was ${result}");
                return Unit();
            });
            Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Waiting Main");
            Thread.Sleep(5000);
            Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Done Main");
        }
    }

    record Person(string name, int age)
    {
        public static Person Peter = new Person("Peter", 88);
    }

    class SucceedNow : ZIOApp<Person>
    {
        ZIO<Person> PeterZIO = ZIO.SucceedNow(Person.Peter);
        public ZIO<Person> Run() => PeterZIO;
    }

    class SucceedNowUhOh : ZIOApp<int>
    {
        static Func<Unit> temp = () => 
        {
            Console.WriteLine("Howdy");
            return Unit();
        };
        // we get eagerly evaluated
        ZIO<Unit> HowdyZIO = ZIO.SucceedNow(temp());
        public ZIO<int> Run() => ZIO.SucceedNow(1);
    }

    class Succeed : ZIOApp<int>
    {
        // we not longer get eagerly evaluated
        // as we have trapped the computation in
        // a function
        ZIO<Unit> HowdyZIO = ZIO.Succeed(() => 
        {
            Console.WriteLine("Howdy");
            return Unit();
        });
        public ZIO<int> Run() => ZIO.SucceedNow(1);
    }

    class SucceedAgain : ZIOApp<Unit>
    {
        ZIO<Unit> WriteLine(string message) => ZIO.Succeed(() => 
        {
            Console.WriteLine(message);
            return Unit();
        });

        public ZIO<Unit> Run() => WriteLine("fancy");
    }

    class Zip : ZIOApp<(int, string)>
    {
        ZIO<(int, string)> ZippedZIO = 
            ZIO.Succeed(() => 8).Zip(ZIO.Succeed(() => "LO"));

        public ZIO<(int, string)> Run() => ZippedZIO;
    }

    class Map : ZIOApp<Person>
    {
        static ZIO<(int, string)> ZippedZIO = 
            ZIO.Succeed(() => 8).Zip(ZIO.Succeed(() => "LO"));
        
        static ZIO<(string, int)> MappedZIO = 
            ZippedZIO.Map<(string, int)>((z) => (z.Item2, z.Item1));

        static ZIO<Person> PersonZIO = 
            ZippedZIO.Map<Person>((z) => new Person(z.Item2, z.Item1));

        public ZIO<Person> Run() => PersonZIO;
    }

    class FlatMap : ZIOApp<Unit>
    {
        static ZIO<(int, string)> ZippedZIO = 
            ZIO.Succeed(() => 8).Zip(ZIO.Succeed(() => "LO"));
        
        static ZIO<Unit> WriteLine(string message) => ZIO.Succeed(() => 
        {
            Console.WriteLine(message);
            return Unit();
        });
        
        static ZIO<Unit> MappedZIO = 
            ZippedZIO.FlatMap<Unit>((z) => WriteLine($"My beautiful tuple {z}"));

        public ZIO<Unit> Run() => MappedZIO;
    }

    class LinqComprehension : ZIOApp<string>
    {
        static ZIO<(int, string)> ZippedZIO = 
            ZIO.Succeed(() => 8).Zip(ZIO.Succeed(() => "LO"));
        
        static ZIO<Unit> WriteLine(string message) => ZIO.Succeed(() => 
        {
            Console.WriteLine(message);
            return Unit();
        });
        
        static ZIO<string> MappedZIO = 
            from z in ZippedZIO
            from _ in WriteLine($"My beautiful tuple {z}")
            select "Nice";

        static ZIO<string> MappedZIORaw = 
            ZippedZIO.FlatMap(z => 
                WriteLine($"My beautiful tuple {z}")
                    .Map((_) => "Nice"));

        static ZIO<string> MappedZIORawAs = 
            ZippedZIO.FlatMap(z => 
                WriteLine($"My beautiful tuple {z}")
                    .As("Nice"));

        public ZIO<string> Run() => MappedZIORawAs;
    }

    class Async : ZIOApp<int>
    {
        // spill our guts
        static ZIO<int> AsyncZIO = 
            ZIO.Async<int>((complete) => 
            {
                Console.WriteLine("Async Start");
                Thread.Sleep(200);
                complete(new Random().Next(999));
                Console.WriteLine("Async End");
                return Unit();
            });

        public ZIO<int> Run() => AsyncZIO;
    }

    class Forked : ZIOApp<string>
    {
        static ZIO<Unit> WriteLine(string message) => ZIO.Succeed(() => 
        {
            Console.WriteLine(message);
            return Unit();
        });

        // spill our guts
        static ZIO<int> AsyncZIO = 
            ZIO.Async<int>((complete) => 
            {
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Client Start");
                Thread.Sleep(2000);
                complete(new Random().Next(999));
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Client End");
                return Unit();
            });

        static ZIO<int> AsyncZIO2 = 
            ZIO.Async<int>((complete) => 
            {
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Client Start");
                Thread.Sleep(3000);
                complete(new Random().Next(999));
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Client End");
                return Unit();
            });

        static ZIO<string> ForkedZIO =
            from fiber1 in AsyncZIO.Fork()
            from fiber2 in AsyncZIO.Fork()
            from _ in  WriteLine($"{Thread.CurrentThread.ManagedThreadId} Nice")
            from i1 in fiber1.Join()
            from i2 in fiber2.Join()
            select $"My beautiful ints {i1} {i2}";

        public ZIO<string> Run() => ForkedZIO;
    }

    class ForkedSync : ZIOApp<string>
    {
        static ZIO<Unit> WriteLine(string message) => ZIO.Succeed(() => 
        {
            Console.WriteLine(message);
            return Unit();
        });

        // spill our guts
        static ZIO<int> SyncZIO = 
            ZIO.Succeed<int>(() => 
            {
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Client Start");
                Thread.Sleep(2000);
                var next = new Random().Next(999);
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Client End");
                return next;
            });

        static ZIO<string> ForkedZIO =
            from fiber1 in SyncZIO.Fork()
            from fiber2 in SyncZIO.Fork()
            from _ in  WriteLine("Nice")
            from i1 in fiber1.Join()
            from i2 in fiber2.Join()
            select $"My beautiful ints {i1} {i2}";

        public ZIO<string> Run() => ForkedZIO;
    }

    class ForkedMain : ZIOApp<string>
    {
        static ZIO<Unit> WriteLine(string message) => ZIO.Succeed(() => 
        {
            Console.WriteLine(message);
            return Unit();
        });

        // spill our guts
        static ZIO<int> AsyncZIO1 = 
            ZIO.Async<int>((complete) => 
            {
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Client Start");
                Thread.Sleep(2000);
                complete(new Random().Next(999));
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Client End");
                return Unit();
            });

        static ZIO<int> AsyncZIO2 = 
            ZIO.Async<int>((complete) => 
            {
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Client Start");
                Thread.Sleep(5000);
                complete(new Random().Next(999));
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Client End");
                return Unit();
            });

        static ZIO<string> ForkedZIO =
            from fiber1 in AsyncZIO2.Fork()
            from _ in  WriteLine($"{Thread.CurrentThread.ManagedThreadId} Nice")
            from i2 in AsyncZIO1
            from i1 in fiber1.Join()
            select $"My beautiful ints {i1} {i2}";

        public ZIO<string> Run() => ForkedZIO;
    }

    class ZipPar : ZIOApp<(int, int)>
    {
        static ZIO<Unit> WriteLine(string message) => ZIO.Succeed(() => 
        {
            Console.WriteLine(message);
            return Unit();
        });

        // spill our guts
        static ZIO<int> AsyncZIO = 
            ZIO.Async<int>((complete) => 
            {
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Client Start");
                Thread.Sleep(200);
                return complete(new Random().Next(999));
            });

        public ZIO<(int, int)> Run() => AsyncZIO.ZipPar(AsyncZIO);
    }
}