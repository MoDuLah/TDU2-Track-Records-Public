using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TDU2_Track_Records
{
    internal sealed class GitHubRelease
    {
        public string tag_name { get; set; }
        public GitHubAsset[] assets { get; set; }
        internal Version Version => GitHubUpdates.ParseVersion(tag_name);
    }

    internal sealed class GitHubAsset
    {
        public long id { get; set; }
        public string name { get; set; }
        public long size { get; set; }
        public string digest { get; set; }
    }

    internal sealed class UpdateFile
    {
        public string path { get; set; }
        public string asset { get; set; }
        public string sha256 { get; set; }
    }

    internal sealed class UpdateManifest
    {
        public int format { get; set; }
        public UpdateFile[] files { get; set; }
    }

    internal sealed class DownloadedUpdate
    {
        internal string Folder { get; set; }
        internal string Message { get; set; }
    }

    internal static class GitHubUpdates
    {
        private static readonly Assembly AppAssembly = typeof(GitHubUpdates).Assembly;
        private static readonly string AppName = AppAssembly.GetName().Name;
        private static readonly string Repo = AppAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "ReleaseRepository").Value;
        private static readonly bool PrivateRepository = Repo.EndsWith("-Private", StringComparison.Ordinal);
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        private const string Json = "application/vnd.github+json";
        private const string Binary = "application/octet-stream";
        private const string Seed = "database.seed.db";
        private static readonly string[] ReleaseFiles =
        {
            AppName + ".exe", AppName + ".exe.config", "System.Data.SQLite.dll",
            "x64/SQLite.Interop.dll", "x86/SQLite.Interop.dll", Seed,
            "Decoder/TDU2TrackRecords.Decoder.exe", "Decoder/LICENSE", "README.md", "THIRD_PARTY_NOTICES.md"
        };

        internal static Version InstalledVersion => AppAssembly.GetName().Version;
        internal static string ReleasesPage => "https://github.com/" + Repo + "/releases";

        internal static Version ParseVersion(string tag)
        {
            if (!Version.TryParse(tag?.TrimStart('v', 'V'), out var parsed) || parsed.Build < 0)
                throw new InvalidDataException("The GitHub release tag is not a Track Records version.");
            return new Version(parsed.Major, parsed.Minor, parsed.Build, Math.Max(0, parsed.Revision));
        }

        internal static string DescribeVersion(Version version) => version.ToString(version.Revision == 0 ? 3 : 4);

        internal static async Task<GitHubRelease> CheckAsync()
        {
            using (var output = new MemoryStream())
            {
                await RequestAsync("releases/latest", Json, output, 25000).ConfigureAwait(false);
                var release = new JavaScriptSerializer().Deserialize<GitHubRelease>(Encoding.UTF8.GetString(output.ToArray()));
                if (release == null || release.assets == null)
                    throw new InvalidDataException("GitHub returned an incomplete release.");
                ParseVersion(release.tag_name);
                return release;
            }
        }

        internal static async Task<DownloadedUpdate> DownloadAsync(GitHubRelease release, string databasePath)
        {
            string prefix = AppName.Replace(' ', '-') + "-v" + DescribeVersion(release.Version) + "-win";
            string install = Path.GetDirectoryName(AppAssembly.Location);
            string root = Path.Combine(Path.GetDirectoryName(databasePath), "updates");
            string folder = Path.Combine(root, DescribeVersion(release.Version) + "-" + Guid.NewGuid().ToString("N"));
            string pending = folder + ".partial";
            string reference = Path.Combine(install, Seed);
            var manifestAsset = release.assets.FirstOrDefault(asset => asset.name == prefix + "-update.json");
            UpdateFile[] changed = null;
            if (manifestAsset != null && manifestAsset.size <= 128 * 1024)
            {
                using (var manifestData = new MemoryStream())
                {
                    await ReadAssetAsync(manifestAsset, manifestData).ConfigureAwait(false);
                    string json = Encoding.UTF8.GetString(manifestData.ToArray()).TrimStart('\uFEFF');
                    var manifest = new JavaScriptSerializer().Deserialize<UpdateManifest>(json);
                    changed = GetChangedFiles(manifest, release, install);
                }
            }

            Directory.CreateDirectory(pending);
            string explanation;
            if (changed != null)
            {
                foreach (var entry in changed)
                {
                    var asset = release.assets.Single(item => item.name == entry.asset);
                    string destination = Path.Combine(pending, InstalledRelativePath(entry.path));
                    await DownloadFileAsync(asset, destination).ConfigureAwait(false);
                    if (entry.path == Seed)
                        reference = destination;
                }
                explanation = $"Downloaded {changed.Length} changed application files.";
            }
            else
            {
                var zip = release.assets.FirstOrDefault(asset => asset.name == prefix + ".zip");
                if (zip == null)
                    throw new InvalidDataException("This GitHub release has no Windows package for this edition.");
                string archive = Path.Combine(pending, prefix + ".zip");
                await DownloadFileAsync(zip, archive).ConfigureAwait(false);
                reference = Path.Combine(pending, "schema-check.db");
                using (var downloaded = ZipFile.OpenRead(archive))
                {
                    var seed = downloaded.Entries.FirstOrDefault(entry =>
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        return name == AppName + "/" + Seed || name == Seed;
                    });
                    if (seed != null && seed.Length <= 128L * 1024 * 1024)
                        seed.ExtractToFile(reference);
                }
                explanation = "Downloaded the full Windows ZIP. This release does not provide a complete set of individual update files.";
            }

            string schema;
            try
            {
                schema = DatabaseMaintenance.Check(databasePath, reference);
            }
            catch (Exception ex) when (ex is IOException || ex is System.Data.SQLite.SQLiteException)
            {
                schema = "Database compatibility check failed. " + ex.Message;
            }
            finally
            {
                if (changed == null && File.Exists(reference))
                    File.Delete(reference);
            }
            Directory.Move(pending, folder);
            return new DownloadedUpdate { Folder = folder, Message = explanation + "\n" + schema };
        }

        private static UpdateFile[] GetChangedFiles(UpdateManifest manifest, GitHubRelease release, string install)
        {
            if (manifest?.format != 1 || manifest.files == null ||
                manifest.files.Any(file => file == null || file.path == null) ||
                manifest.files.Length != ReleaseFiles.Length ||
                !new HashSet<string>(ReleaseFiles, StringComparer.Ordinal).SetEquals(manifest.files.Select(file => file.path)))
                throw new InvalidDataException("The release manifest does not match the Track Records package.");

            var changed = new List<UpdateFile>();
            foreach (var file in manifest.files)
            {
                string local = Path.Combine(install, InstalledRelativePath(file.path));
                if (File.Exists(local))
                {
                    using (var input = File.OpenRead(local))
                    {
                        if (MatchesHash(input, file.sha256))
                            continue;
                    }
                }
                var asset = release.assets.SingleOrDefault(item => item.name == file.asset);
                if (asset == null)
                    return null;
                if (string.IsNullOrEmpty(file.sha256) ||
                    !string.Equals(asset.digest, "sha256:" + file.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A release file has an invalid checksum in the update manifest.");
                changed.Add(file);
            }
            return changed.ToArray();
        }

        private static string InstalledRelativePath(string releasePath)
        {
            string name = Path.GetFileName(AppAssembly.Location);
            if (releasePath == AppName + ".exe")
                return name;
            if (releasePath == AppName + ".exe.config")
                return name + ".config";
            return releasePath.Replace('/', Path.DirectorySeparatorChar);
        }

        private static async Task DownloadFileAsync(GitHubAsset asset, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".partial";
            try
            {
                using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                    await ReadAssetAsync(asset, destination).ConfigureAwait(false);
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static async Task ReadAssetAsync(GitHubAsset asset, Stream destination)
        {
            if (asset.id <= 0 || asset.size <= 0 || asset.size > 512L * 1024 * 1024 ||
                asset.digest == null || !asset.digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GitHub did not provide a usable checksum for this release asset.");
            await RequestAsync("releases/assets/" + asset.id, Binary, destination, 180000).ConfigureAwait(false);
            destination.Position = 0;
            if (destination.Length != asset.size || !MatchesHash(destination, asset.digest.Substring(7)))
                throw new InvalidDataException("The downloaded file did not match GitHub's release checksum.");
        }

        private static bool MatchesHash(Stream stream, string expected)
        {
            using (var sha = SHA256.Create())
                return string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""),
                    expected, StringComparison.OrdinalIgnoreCase);
        }

        private static async Task RequestAsync(string endpoint, string accept, Stream destination, int timeout)
        {
            string route = "repos/" + Repo + "/" + endpoint;
            if (PrivateRepository)
            {
                await RequestWithGitHubCliAsync(route, accept, destination, timeout).ConfigureAwait(false);
                return;
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/" + route))
            using (var cancellation = new System.Threading.CancellationTokenSource(timeout))
            {
                request.Headers.UserAgent.ParseAdd("TDU2-Track-Records");
                request.Headers.Accept.ParseAdd(accept);
                using (var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token)
                    .ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound && endpoint == "releases/latest")
                        throw new InvalidOperationException("GitHub did not return a published release.");
                    response.EnsureSuccessStatusCode();
                    using (var content = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        await content.CopyToAsync(destination, 81920, cancellation.Token).ConfigureAwait(false);
                }
            }
        }

        private static async Task RequestWithGitHubCliAsync(string route, string accept, Stream destination, int timeout)
        {
            string cli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe");
            if (!File.Exists(cli))
                cli = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
                    .Select(folder => folder.Trim().Trim('"')).Where(Path.IsPathRooted)
                    .Select(folder => Path.Combine(folder, "gh.exe")).FirstOrDefault(File.Exists);
            if (cli == null)
                throw new InvalidOperationException("The Private release check uses GitHub CLI. Install it and sign in to an account with access, or open GitHub Releases.");
            var start = new ProcessStartInfo
            {
                FileName = cli,
                Arguments = "api --hostname github.com -H Accept:" + accept + " " + route,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.EnvironmentVariables["GH_PROMPT_DISABLED"] = "1";
            start.EnvironmentVariables.Remove("GH_DEBUG");
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.BaseStream.CopyToAsync(destination);
                var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
                bool exited = await Task.Run(() => process.WaitForExit(timeout)).ConfigureAwait(false);
                if (!exited)
                    process.Kill();
                await Task.WhenAll(output, error).ConfigureAwait(false);
                if (!exited)
                    throw new TimeoutException("The GitHub request timed out.");
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("GitHub could not return the Private release or download. Check that GitHub CLI is signed in with access, or open GitHub Releases.");
            }
        }
    }
}
