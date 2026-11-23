using System.Text;
using System.Web;
using ClaimsModule.Application.Abstractions.Audit;
using ClaimsModule.Application.Abstractions.Storage;
using ClaimsModule.Application.Audit;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Documents;
using ClaimsModule.Infrastructure.Storage;
using ClaimsModule.IntegrationTests.Persistence;
using Microsoft.Extensions.Options;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>Task 29: document upload and listing through the pipeline, and both storage providers.</summary>
[Collection(SqlServerTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "_host is disposed in IAsyncLifetime.DisposeAsync, which xUnit calls after every test.")]
public sealed class DocumentTests(SqlServerDatabaseFixture db) : IAsyncLifetime
{
    private ApplicationHost _host = null!;

    public Task InitializeAsync()
    {
        _host = new ApplicationHost(db);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        if (Directory.Exists(_host.StorageRoot))
        {
            Directory.Delete(_host.StorageRoot, recursive: true);
        }
    }

    private static UploadDocumentCommand Upload(Guid claimId, string fileName, string contentType, string text = "Police report no. 4711", string? key = null)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new UploadDocumentCommand(claimId, "PoliceReport", fileName, contentType, bytes.Length)
        {
            Content = new MemoryStream(bytes),
            IdempotencyKey = key,
        };
    }

    private string LocalFile(DocumentDto document) =>
        Path.Combine(_host.StorageRoot, HttpUtility.UrlDecode(document.DownloadUrl.AbsolutePath["/uploads/".Length..]).Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public async Task Upload_stores_the_file_registers_metadata_audits_and_lists_with_a_link()
    {
        var claimId = (await _host.SendAsync(Fnol.Complete(_host.Clock.Now))).ClaimId;

        var uploaded = await _host.SendAsync(Upload(claimId, "../../Police Report?.txt", "text/plain"));

        uploaded.DocumentName.ShouldBe("Police Report_.txt"); // sanitised (BR-D-01)
        uploaded.ContentType.ShouldBe("text/plain");
        uploaded.DownloadUrlExpiresAt.ShouldBe(_host.Clock.Now.AddHours(1));
        uploaded.DownloadUrl.AbsolutePath.ShouldStartWith($"/uploads/{SqlServerDatabaseFixture.OrganisationId:D}/{claimId:D}/{uploaded.DocumentId:N}-");
        (await File.ReadAllTextAsync(LocalFile(uploaded))).ShouldBe("Police report no. 4711");

        var listed = await _host.SendAsync(new ListClaimDocumentsQuery(claimId));
        listed.Single().DocumentId.ShouldBe(uploaded.DocumentId);
        listed.Single().UploadedByName.ShouldBe(ApplicationHost.Handler1.DisplayName);

        var audit = (await _host.SendAsync(new GetAuditLogQuery(claimId) { EventType = AuditEventTypes.DocumentUploaded })).Items.Single();
        audit.RelatedEntityId.ShouldBe(uploaded.DocumentId);
    }

    [Theory]
    [InlineData("malware.exe", "application/octet-stream", "DOCUMENT_TYPE_NOT_ALLOWED")]
    [InlineData("photo.png", "application/pdf", "DOCUMENT_TYPE_NOT_ALLOWED")]
    public async Task Disallowed_files_are_refused_and_nothing_is_stored(string fileName, string contentType, string code)
    {
        var claimId = (await _host.SendAsync(Fnol.Complete(_host.Clock.Now))).ClaimId;

        var error = await Should.ThrowAsync<RequestValidationException>(() => _host.SendAsync(Upload(claimId, fileName, contentType)));

        error.Violations.Single().Code.ShouldBe(code);
        (await _host.SendAsync(new ListClaimDocumentsQuery(claimId))).ShouldBeEmpty();
        Directory.Exists(Path.Combine(_host.StorageRoot, SqlServerDatabaseFixture.OrganisationId.ToString("D"), claimId.ToString("D"))).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0L, "DOCUMENT_EMPTY")]
    [InlineData(50L * 1024 * 1024 + 1, "DOCUMENT_TOO_LARGE")]
    public async Task Empty_and_oversized_files_are_refused(long declaredLength, string code)
    {
        var claimId = (await _host.SendAsync(Fnol.Complete(_host.Clock.Now))).ClaimId;
        var command = new UploadDocumentCommand(claimId, "Photo", "big.pdf", "application/pdf", declaredLength) { Content = new MemoryStream([1]) };

        (await Should.ThrowAsync<RequestValidationException>(() => _host.SendAsync(command))).Violations.Single().Code.ShouldBe(code);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_first_upload_without_storing_again()
    {
        var claimId = (await _host.SendAsync(Fnol.Complete(_host.Clock.Now))).ClaimId;
        var key = Guid.NewGuid().ToString();

        var first = await _host.SendAsync(Upload(claimId, "report.pdf", "application/pdf", key: key));
        var replay = await _host.SendAsync(Upload(claimId, "report.pdf", "application/pdf", key: key));

        replay.DocumentId.ShouldBe(first.DocumentId);
        (await _host.SendAsync(new ListClaimDocumentsQuery(claimId))).Count.ShouldBe(1);
        Directory.GetFiles(Path.GetDirectoryName(LocalFile(first))!).Length.ShouldBe(1);
    }

    [Fact]
    public async Task Upload_to_an_unknown_claim_is_404()
    {
        await Should.ThrowAsync<NotFoundException>(() => _host.SendAsync(Upload(Guid.NewGuid(), "report.pdf", "application/pdf")));
        await Should.ThrowAsync<NotFoundException>(() => _host.SendAsync(new ListClaimDocumentsQuery(Guid.NewGuid())));
    }

    // ------------------------------------------------------------------ providers

    [Fact]
    public void Azure_blob_read_link_is_a_one_hour_read_only_https_sas_for_one_blob()
    {
        // A syntactically valid account key; generating a SAS needs no network call.
        var key = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('k', 64)));
        var options = Options.Create(new StorageOptions
        {
            Provider = StorageProvider.AzureBlob,
            AzureBlob = { ConnectionString = $"DefaultEndpointsProtocol=https;AccountName=claimstest;AccountKey={key};EndpointSuffix=core.windows.net" },
        });
        var clock = new TestClock(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        var storage = new AzureBlobStorageService(options, clock);

        var uri = storage.GetReadUri("org/claim/doc-report.pdf", TimeSpan.FromHours(1));
        var query = HttpUtility.ParseQueryString(uri.Query);

        uri.GetLeftPart(UriPartial.Path).ShouldBe("https://claimstest.blob.core.windows.net/claim-documents/org/claim/doc-report.pdf");
        query["sp"].ShouldBe("r"); // read only
        query["sr"].ShouldBe("b"); // one blob, not the container
        query["spr"].ShouldBe("https");
        DateTimeOffset.Parse(query["se"]!, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(clock.Now.AddHours(1));
        query["sig"].ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Azure_blob_provider_refuses_a_connection_string_without_an_account_key()
    {
        var options = Options.Create(new StorageOptions
        {
            AzureBlob = { ConnectionString = "BlobEndpoint=https://claimstest.blob.core.windows.net/;SharedAccessSignature=sv=2024-01-01&sig=x" },
        });

        Should.Throw<InvalidOperationException>(() => new AzureBlobStorageService(options, TimeProvider.System)).Message.ShouldContain("no account key");
    }

    [Fact]
    public async Task Local_provider_refuses_paths_that_escape_its_folder()
    {
        var storage = new LocalFileSystemStorageService(Options.Create(new StorageOptions
        {
            LocalFileSystem = { RootPath = _host.StorageRoot },
        }));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            storage.UploadAsync("../outside.txt", new MemoryStream([1]), "text/plain", default));
    }
}
