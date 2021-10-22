using System;
using Unit = System.ValueTuple;
using LaYumba.Functional;
using static LaYumba.Functional.F;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace Zio
{
    static class ZIOWorkflow
    {
        public static ZIO<B> Select<A, B>
           (this ZIO<A> self, Func<A, B> f)
           => self.Map(f);

        public static ZIO<B> SelectMany<A, B>
           (this ZIO<A> self, Func<A, ZIO<B>> f)
           => self.FlatMap(f);

        public static ZIO<BB> SelectMany<A, B, BB>
           (this ZIO<A> self, Func<A, ZIO<B>> f, Func<A, B, BB> project)
           => self.FlatMap(a => f(a).Map(b => project(a, b)));
    }

    interface Fiber<A>
    {
        // early completion
        // late completion
        ZIO<A> Join();
        Unit Start();
    }

    class FiberImpl<A> : Fiber<A>
    {
        private ZIO<A> zio;
        private Option<A> maybeResult;

        private List<Func<A, Unit>> callbacks = new List<Func<A, Unit>>();

        public FiberImpl(ZIO<A> zio)
        {
            this.zio = zio;
        }

        public ZIO<A> Join() =>
            this.maybeResult.Match(
                ()  => ZIO.Async<A>(complete => 
                {
                    Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Join Register");
                    this.callbacks.Add(complete);
                    return Unit();
                }),
                (a) => 
                {
                     Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Join Complete");
                     return ZIO.SucceedNow<A>(a);
                }
            );

        public Unit Start()
        {
            Task.Run(() => 
            {
                this.zio.Run((a) => {
                    Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Result:{a}");
                    this.maybeResult = Some(a);
                    this.callbacks.ForEach(callback => callback(a));
                    return Unit();
                });
            });
            return Unit();
        }
            
    }

    class FiberImplAwait<A> : Fiber<A>
    {
        interface FiberState {}
        class Running : FiberState
        {
            public List<Func<A, Unit>> Callbacks { get; }
            public Running(List<Func<A, Unit>> callbacks)
            {
                this.Callbacks = callbacks;
            }
        }
        class Done : FiberState
        {
            public A Result { get; }
            public Done(A result)
            {
                this.Result = result;
            }
        }
        // CAS
        // compare and swap
        private Akka.Util.AtomicReference<FiberState> state = 
            new Akka.Util.AtomicReference<FiberState>(new Running(new List<Func<A, Unit>>()));

        public Unit Complete(A result)
        {
            var loop = true;
            var toComplete = new List<Func<A, Unit>>();
            while (loop)
            {
                var oldState = state.Value;
                switch (oldState)
                {
                    case Running running:
                        //Console.WriteLine("Complete Pending callbacks count:" + running.Callbacks.Count());
                        toComplete = running.Callbacks;
                        var tryAgain = !state.CompareAndSet(oldState, new Done(result));
                        //Console.WriteLine("Complete tryAgain:" + tryAgain);
                        loop = tryAgain;
                        break;
                    case Done done:
                        throw new Exception("Fiber being completed multiple times");
                }
            }
            toComplete.ForEach(callback => callback(result));
            
            return Unit();
        }

        public Unit Await(Func<A, Unit> callback)
        {
            //Console.WriteLine("Await with our callback");
            var loop = true;
            while (loop)
            {
                var oldState = state.Value;
                switch (oldState)
                {
                    case Running running:
                        //Console.WriteLine("Await already running Count:" + running.Callbacks.Count());
                        running.Callbacks.Add(callback);
                        var newState = new Running(running.Callbacks);
                        loop = !state.CompareAndSet(oldState, newState);
                        Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Join Register");
                        break;
                    case Done done:
                        Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Join Done");
                        callback(done.Result);
                        loop = false;
                        break;
                }
            }
            return Unit();
        }

        private ZIO<A> zio;

        public FiberImplAwait(ZIO<A> zio)
        {
            this.zio = zio;
        }

        public ZIO<A> Join() =>
            ZIO.Async<A>(callback => 
            {
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Join Before Await");
                Await(callback);
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Join After Await");
                return Unit();
            });

        public Unit Start()
        {
            Task.Run(() =>
            { 
                this.zio.Run(a => 
                {
                    Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Result:{a}");
                    Complete(a);
                    return Unit();
                });
            });
            return Unit();
        }   
    }

    // Declarative Encoding
    // ZIO<R, E, A>
    // async
    interface ZIO<A> 
    {
        // continuation
        // callback
        // method A => Unit
        Unit Run(Func<A, Unit> callback);

        ZIO<B> As<B>(B b) => 
            this.Map((_) => b);

        ZIO<B> FlatMap<B>(Func<A, ZIO<B>> f) => 
            new FlatMap<A, B>(this, f);

        // correct by construction
        ZIO<Fiber<A>> Fork() =>
            new Fork<A>(this);

        ZIO<B> Map<B>(Func<A, B> f) => 
            this.FlatMap((a) => ZIO.SucceedNow(f(a)));

        ZIO<(A, B)> Zip<B>(ZIO<B> that) => 
            from a in this
            from b in that
            select (a, b);

        ZIO<(A, B)> ZipPar<B>(ZIO<B> that) => 
            from f in this.Fork()
            from b in that
            from a in f.Join()
            select (a, b);
    }

    class Succeed<A> : ZIO<A>
    {
        private A value;
        public Succeed(A value)
        {
            this.value = value;
        }

        public Unit Run(Func<A, Unit> callback)
            => callback(this.value);
    }

    class Effect<A> : ZIO<A>
    {
        private Func<A> f;
        public Effect(Func<A> f)
        {
            this.f = f;
        }

        public Unit Run(Func<A, Unit> callback)
            => callback(this.f());
    }

    class FlatMap<A, B> : ZIO<B>
    {
        private ZIO<A> a;
        private Func<A, ZIO<B>> f;
        public FlatMap(ZIO<A> a, Func<A, ZIO<B>> f)
        {
            this.a = a;
            this.f = f;
        }

        public Unit Run(Func<B, Unit> callback)
        {
            return this.a.Run(a => 
            {
                return f(a).Run(callback);
            });
        }
    }

    class Async<A> : ZIO<A>
    {
        private Func<Func<A, Unit>, Unit> register;
        public Async(Func<Func<A, Unit>, Unit> register)
        {
            this.register = register;
        }

        public Unit Run(Func<A, Unit> callback)
        {
            Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Async Run Start");
            this.register(callback);
            Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} Async Run End");
            return Unit();
        }
    }

    class Fork<A> : ZIO<Fiber<A>>
    {
        private ZIO<A> zio;
        public Fork(ZIO<A> zio)
        {
            this.zio = zio;
        }

        public Unit Run(Func<Fiber<A>, Unit> callback)
        {
            var fiber = new FiberImplAwait<A>(zio);
            fiber.Start();
            return callback(fiber);
        }
    }

    static class ZIO
    {
        public static ZIO<A> Async<A>(Func<Func<A, Unit>, Unit> register) =>
            new Async<A>(register);
        public static ZIO<A> Succeed<A>(Func<A> value) => 
            new Effect<A>(value);
        internal static ZIO<A> SucceedNow<A>(A value) => 
            new Succeed<A>(value);

    }
}