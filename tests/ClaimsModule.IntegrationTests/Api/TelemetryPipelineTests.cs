using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using ClaimsModule.API.Hosting;
using ClaimsModule.Infrastructure.Jobs;
using ClaimsModule.IntegrationTests.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace ClaimsModule.IntegrationTests.Api;

/// <summary>
/// What reaches App Insights (P5, D-71): the startup warm-up line is exported, and SQL with no parent
/// operation (Hangfire polling, the warm-up) is not, while SQL inside requests and job runs is.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed partial class TelemetryPipelineTests(SqlServerDatabaseFixture db)
{
    [Fact]
    public async Task The_real_exporter_sends_the_warm_up_line_and_request_sql_but_no_parentless_sql()
    {
        // The real Azure Monitor exporter against a local fake ingestion endpoint. With the warm-up
        // before RunAsync (P4) the OpenTelemetry pipeline saw the line but the exporter never sent it.
        await using var ingestion = new FakeIngestionEndpoint();
        await using var factory = new ClaimsApiFactory(db, new Dictionary<string, string?>
        {
            // A fresh key per run: the exporter keeps failed batches on disk per key and resends them later.
            [Telemetry.ConnectionStringKey] = $"InstrumentationKey={Guid.NewGuid()};IngestionEndpoint={ingestion.BaseUrl}",
            [StartupWarmUp.SettingKey] = "true", // its SQL has no parent operation
        });
        using var client = factory.CreateClient();
        (await client.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Wait while the host runs: batches are exported on a schedule; shutdown flushing is unreliable.
        // Request SQL may be sampled out (rate-limited sampler), so only the warm-up line is awaited; the
        var received = await ingestion.WaitForAsync(
            body => body.Contains("Startup warm-up: database health Healthy", StringComparison.Ordinal),
            TimeSpan.FromSeconds(30));

        // test below checks recorded vs unrecorded SQL in the host itself.
        received.ShouldContain("Startup warm-up: database health Healthy");
        SqlDependencies(received).ShouldAllBe(d => d.HasParent);
    }

    [Fact]
    public async Task In_the_host_parentless_sql_is_unrecorded_and_sql_inside_a_request_is_recorded()
    {
        // An independent listener: it sees every SQL activity as it stops, including unrecorded ones the
        // SDK no longer passes to processors. PropagationData never upgrades the SDK's sampling decision.
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BackgroundSqlNoiseFilter.SqlClientSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData,
            ActivityStopped = activity =>
            {
                lock (stopped)
                {
                    stopped.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        await using (var factory = new ClaimsApiFactory(db, new Dictionary<string, string?>
        {
            [Telemetry.ConnectionStringKey] = $"InstrumentationKey={Guid.NewGuid()};IngestionEndpoint=https://127.0.0.1:1/",
            [StartupWarmUp.SettingKey] = "true", // the warm-up's SQL has no parent
        }))
        {
            using var client = factory.CreateClient();
            // SQL inside requests. The distro's rate-limited sampler starts with no budget, so the first
            // request after start-up isn't sampled: repeat until one is (about a second).
            for (var attempt = 0; attempt < 40 && !AnyRecordedRequestSql(); attempt++)
            {
                (await client.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }

        bool AnyRecordedRequestSql()
        {
            lock (stopped)
            {
                return stopped.Any(a => a.Parent is not null && a.Recorded);
            }
        }

        List<Activity> sql;
        lock (stopped)
        {
            // Guards the operation name the filter relies on: the distro's SqlClient instrumentation uses it.
            sql = stopped.Where(a => a.OperationName == BackgroundSqlNoiseFilter.SqlClientOperationName).ToList();
        }

        var parentless = sql.Where(a => a.Parent is null && a.ParentSpanId == default).ToList();
        var inRequest = sql.Except(parentless).ToList();

        parentless.ShouldNotBeEmpty();
        parentless.ShouldAllBe(a => !a.Recorded); // the exporter only sends recorded activities
        inRequest.ShouldNotBeEmpty();
        // Left alone by the filter: it follows its request's sampling decision (the distro's rate-limited
        // sampler may drop a whole request trace, so "recorded" itself isn't guaranteed).
        inRequest.ShouldAllBe(a => a.Parent != null && a.Recorded == a.Parent.Recorded);
        inRequest.ShouldContain(a => a.Recorded);
    }

    [Fact]
    public async Task Sql_inside_a_job_run_is_recorded_and_parentless_sql_is_not()
    {
        var capture = new ActivityCapture();
        using var sqlSource = new ActivitySource(BackgroundSqlNoiseFilter.SqlClientSourceName);
        using var tracer = Sdk.CreateTracerProviderBuilder()
            .AddSource(BackgroundSqlNoiseFilter.SqlClientSourceName, JobTelemetry.ActivitySourceName)
            .AddProcessor(new BackgroundSqlNoiseFilter())
            .AddProcessor(capture)
            .Build();

        Activity? polling;
        using (polling = sqlSource.StartActivity(BackgroundSqlNoiseFilter.SqlClientOperationName))
        {
        }

        Activity? jobSql = null;
        await JobTelemetry.RunAsync("GL posting", "42", () =>
        {
            using (jobSql = sqlSource.StartActivity(BackgroundSqlNoiseFilter.SqlClientOperationName))
            {
            }

            return Task.CompletedTask;
        });

        polling.ShouldNotBeNull().Recorded.ShouldBeFalse();
        capture.HasEnded(polling).ShouldBeFalse();
        jobSql.ShouldNotBeNull().Recorded.ShouldBeTrue();
        capture.HasEnded(jobSql).ShouldBeTrue();
        var job = capture.Started.Single(a => a.Source.Name == JobTelemetry.ActivitySourceName);
        jobSql.Parent.ShouldBeSameAs(job);
        job.DisplayName.ShouldBe("Job GL posting");
        job.GetTagItem("job.id").ShouldBe("42");
        capture.HasEnded(job).ShouldBeTrue();
    }

    [Fact]
    public async Task A_failing_job_run_is_marked_as_an_error()
    {
        var capture = new ActivityCapture();
        using var tracer = Sdk.CreateTracerProviderBuilder().AddSource(JobTelemetry.ActivitySourceName).AddProcessor(capture).Build();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            JobTelemetry.RunAsync("sla-monitoring", null, () => throw new InvalidOperationException("boom")));

        capture.Started.Single().Status.ShouldBe(ActivityStatusCode.Error);
    }

    private sealed record SqlDependency(bool HasParent);

    /// <summary>The SQL dependency items in exported batches (one JSON item per line).</summary>
    private static List<SqlDependency> SqlDependencies(string batches) =>
        batches.Split('\n')
            .Where(line => line.Contains("\"name\":\"RemoteDependency\"", StringComparison.Ordinal) && SqlType().IsMatch(line))
            .Select(line => new SqlDependency(line.Contains("\"ai.operation.parentId\"", StringComparison.Ordinal)))
            .ToList();

    [GeneratedRegex("\"type\":\"SQL\"")]
    private static partial Regex SqlType();

    /// <summary>Keeps every activity the provider starts (after the noise filter ran) and which of them ended.</summary>
    private sealed class ActivityCapture : BaseProcessor<Activity>
    {
        private readonly List<Activity> _started = [];
        private readonly HashSet<Activity> _ended = [];

        public IReadOnlyList<Activity> Started
        {
            get
            {
                lock (_started)
                {
                    return [.. _started];
                }
            }
        }

        public bool HasEnded(Activity activity)
        {
            lock (_started)
            {
                return _ended.Contains(activity);
            }
        }

        public override void OnStart(Activity data)
        {
            lock (_started)
            {
                _started.Add(data);
            }
        }

        // The SDK only calls OnEnd for activities whose data is requested: unrecorded noise never gets here.
        public override void OnEnd(Activity data)
        {
            lock (_started)
            {
                _ended.Add(data);
            }
        }
    }

    /// <summary>A local stand-in for the App Insights ingestion endpoint: accepts and keeps every batch.</summary>
    private sealed class FakeIngestionEndpoint : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly List<string> _bodies = [];
        private readonly Task _loop;

        public FakeIngestionEndpoint()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            BaseUrl = $"http://localhost:{port}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _loop = Task.Run(AcceptAsync);
        }

        public string BaseUrl { get; }

        public string Received()
        {
            lock (_bodies)
            {
                return string.Join('\n', _bodies);
            }
        }

        public async Task<string> WaitForAsync(Func<string, bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                var received = Received();
                if (condition(received) || DateTime.UtcNow >= deadline)
                {
                    return received;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _loop;
            _listener.Close();
        }

        private async Task AcceptAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                Stream body = context.Request.InputStream;
                if (context.Request.Headers["Content-Encoding"] == "gzip")
                {
                    body = new GZipStream(body, CompressionMode.Decompress);
                }

                using var reader = new StreamReader(body);
                var text = await reader.ReadToEndAsync();
                lock (_bodies)
                {
                    _bodies.Add(text);
                }

                var items = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                var response = Encoding.UTF8.GetBytes($$"""{"itemsReceived":{{items}},"itemsAccepted":{{items}},"errors":[]}""");
                context.Response.StatusCode = 200;
                await context.Response.OutputStream.WriteAsync(response);
                context.Response.Close();
            }
        }
    }
}