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
            SentrySdk.ConfigureScope(scope => scope.AddTag("Initial scope data."));

            SentrySdk.WithClientAndScope((client, scope) =>
            {
                // Create heavy event stuff
                var evt = new SentryEvent("Entrypoint event.");
                scope.AddTag("Some scope change done in the callback");
                return client.CaptureEvent(evt, scope);
            });

            // TODO: how does it behave with .ConfigureAwait(false);
            var task = Task.Run(() =>
            {
                // If no scope is pushed here, it'd be mutating the outer scope
                using (SentrySdk.PushScope()) // New scope, clone of the parent
                {
                    // Should it be ConfigureNewScope instead and bundle operations?
                    SentrySdk.ConfigureScope(scope => scope.AddTag("First TPL task adding to scope"));

                    // Simply send event
                    SentrySdk.CaptureEvent(new SentryEvent("First Event from TPL"));
                }
            });

            await task;

            // here we shouldn't see side-effect from the TPL task
            SentrySdk.CaptureEvent(new SentryEvent("Final event from main thread"));

            throw new Exception("Error in the app");
        }
    }
}
