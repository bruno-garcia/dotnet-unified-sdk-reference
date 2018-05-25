using System;
using System.Threading.Tasks;
using Sentry.Protocol;
using Sentry;

namespace Company.App
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // No SDK is active: Callback should not be invoked!
            SentrySdk.WithClientAndScope((client, scope) =>
            {
                // Create heavy event stuff
                var evt = new SentryEvent("Don't run me");

                return client.CaptureEvent(evt, scope);
            });

            // Again, as the SDK is not enabled, callback is never invoked.
            SentrySdk.ConfigureScope(scope => scope.AddTag(SuperHeavyMethod()));
            string SuperHeavyMethod() => throw new InvalidOperationException("Don't call me!");

            // Program.Main should be:
            SentrySdk.Init(o =>
            {
                // Some options
                o.CompressPayload = false;
                o.ShutdownTimeout = TimeSpan.FromSeconds(5);
            });

            try
            {
                SentrySdk.ConfigureScope(scope => scope.AddTag("Global scope thingy."));

                using (SentrySdk.PushScope())
                {
                    SentrySdk.ConfigureScope(scope => scope.AddTag("Only visible within App!"));

                    await App();

                    SentrySdk.ConfigureScope(scope => scope.AddTag("Will never be seen at all."));
                }
            }
            catch (Exception exception)
            {
                var id = SentrySdk.CaptureException(exception);
                Console.WriteLine("Id: " + id);

                id = await SentrySdk.CaptureExceptionAsync(exception);
                Console.WriteLine("Id: " + id);
            }
            finally
            {
                SentrySdk.CloseAndFlush();
            }

            // SDK can be reinitialized
            SentrySdk.Init();
            // A second call will throw away the previous one and get a new one
            SentrySdk.Init();
            try
            {
                throw null;
            }
            catch (Exception exception)
            {
                var id = SentrySdk.CaptureException(exception);
                Console.WriteLine("Id: " + id);
            }
            finally
            {
                SentrySdk.CloseAndFlush();
            }

            // Finally, Closing a disabled SDK is a no-op
            SentrySdk.CloseAndFlush();

            // Proposed API
            await ProposedApi();
        }

        static async Task ProposedApi()
        {
            SentrySdk.Init();
            SentrySdk.ConfigureScope(s => s.AddTag("Some proposed APIs"));

            // if the goal is avoiding execution of the callback when the SDK is disabled
            // the simplest API is a delegate to create the event

            // Async is best if underlying client is doing I/O (writing event to disk or sending via HTTP)
            var id = await SentrySdk.CaptureEventAsync(async () =>
            {
                // NOT invoked if the SDK is disabled
                var dbStuff = await DatabaseQuery();
                return new SentryEvent(dbStuff);
            });
            Console.WriteLine("Id: " + id);

            // Blocking alternative (best if using in-memory queue)
            id = SentrySdk.CaptureEvent(() => new SentryEvent("Nothing async in this callback"));
            Console.WriteLine("Id: " + id);
        }

        private static Task<string> DatabaseQuery()
        {
            return Task.FromResult("something heavy to retrieve");
        }

        private static async Task App()
        {
            SentrySdk.ConfigureScope(scope => scope.AddTag("Starting app.."));

            SentrySdk.WithClientAndScope((client, scope) =>
            {
                // Create heavy event stuff
                var evt = new SentryEvent("First App event.");
                scope.AddTag("Scope data from within callback.");
                return client.CaptureEvent(evt, scope);
            });

            // TODO: how does it behave with .ConfigureAwait(false);
            var task = Task.Run(() =>
            {
                SentrySdk.ConfigureScope(scope => scope.AddTag(@"Data set inside Task but outside any scope guard. 
Should survive the Task itself"));

                using (SentrySdk.PushScope()) // New scope, clone of the parent
                {
                    SentrySdk.ConfigureScope(scope => scope.AddTag("A"));
                    SentrySdk.CaptureEvent(new SentryEvent("A"));

                    using (SentrySdk.PushScope()) // New scope, clone of the parent
                    {
                        SentrySdk.ConfigureScope(scope => scope.AddTag("B1"));
                        SentrySdk.CaptureEvent(new SentryEvent("B1"));
                    }

                    using (SentrySdk.PushScope()) // New scope, clone of the parent
                    {
                        SentrySdk.ConfigureScope(scope => scope.AddTag("B2"));
                        SentrySdk.CaptureEvent(new SentryEvent("B2"));
                    }
                }

            });

            await task;

            SentrySdk.CaptureEvent(new SentryEvent("Event after awaiting TPL task"));

            throw new Exception("Error in the app");
        }
    }
}
