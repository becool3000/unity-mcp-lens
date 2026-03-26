using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEditor;

namespace Unity.AI.Assistant.Tools.Editor
{
    class WebFetchTools
    {
        const string k_WebFetchFunctionId = "Unity.Web.Fetch";
        const int k_MaxContentSizePerUrl = 100000; // ~100KB per URL
        const int k_TimeoutSeconds = 30;

        // Unity domains use a full browser UA because Discourse forums return 403 to
        // non-browser agents. External domains use a transparent UA that identifies the
        // product while remaining compatible with most servers.
        const string k_BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        const string k_TransparentUserAgent =
            "Mozilla/5.0 (compatible; UnityAIAssistant/1.0; +https://unity.com)";

        const string k_GroundingInstructions =
            "This content was fetched live from the web.\n" +
            "GROUNDING RULES:\n" +
            "1. If official Unity documentation (docs.unity3d.com, unity.com) clearly states something, " +
            "trust it as the current fact. For community sources or ambiguous content, present BOTH what " +
            "you know and what this page says — look for dates (post timestamps, version numbers) to assess recency.\n" +
            "2. If this is from a community source (forum posts, third-party tutorials), acknowledge that when citing it.\n" +
            "3. If this content indicates a feature has been removed, deprecated, or changed, " +
            "you MUST communicate this to the user.\n" +
            "4. Never suppress what you found here. If this content is relevant, include it in your response.\n\n" +
            "INCLUDE the useful links (urls) in your final answer, citing the links naturally in the middle " +
            "of your final answer, in the relevant parts, and not at the end.\n" +
            "DO NOT create a footnote section with the links at the end of your response.\n" +
            "If you use a link, cite it with an index in brackets like \"[x]\" where \"x\" is the index of " +
            "the link as returned by the fetch tool.\n" +
            "Do not put link text between square brackets, don't do: [some text] [1]\n";

        static readonly HttpClient s_HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(k_TimeoutSeconds)
        };

        static bool IsUnityHost(Uri uri)
        {
            var host = uri.Host;
            return host.EndsWith(".unity.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".unity3d.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("unity.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("unity3d.com", StringComparison.OrdinalIgnoreCase);
        }

        static string GetUserAgent(Uri uri)
        {
            return IsUnityHost(uri) ? k_BrowserUserAgent : k_TransparentUserAgent;
        }

        [Serializable]
        public struct FetchedPage
        {
            [Description("The URL that was fetched")]
            public string Url;

            [Description("The title extracted from the page")]
            public string Title;

            [Description("The content extracted from the page")]
            public string Content;
        }

        [AgentTool(
            "Fetches and extracts content from web pages. Converts HTML to markdown format with citation indices for LLM consumption. Supports HTTP and HTTPS URLs.",
            k_WebFetchFunctionId,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag)]
        public static async Task<string> WebFetch(
            ToolExecutionContext context,
            [Parameter(
                "URL to fetch and extract content from. " +
                "Example: \"https://docs.unity3d.com/Manual/index.html\""
            )]
            string url
        )
        {
            InternalLog.Log($"[WebFetchTools] WebFetch called with URL: {url}");

            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentException("No URL provided. Please provide a valid HTTP/HTTPS URL.");
            }

            // Domain reload would abandon the in-flight HTTP task and the tool
            // response would never be sent back, hanging the conversation.
            // Locking assemblies defers any pending reload until the fetch completes
            // or times out (bounded by k_TimeoutSeconds).
            EditorApplication.LockReloadAssemblies();
            try
            {
                var fetchResult = await FetchUrl(url);

                var output = new StringBuilder();
                output.AppendLine($"[1]: {fetchResult.Title}");
                output.AppendLine(fetchResult.Content);
                output.AppendLine();
                output.Append(k_GroundingInstructions);

                return output.ToString();
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
            }
        }

        static async Task<FetchedPage> FetchUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Invalid URL format. Only HTTP/HTTPS URLs are supported.");
            }

            // Discourse forums use JavaScript lazy-loading, so a plain GET returns an
            // empty shell. Use their JSON API instead to get actual post content.
            if (IsDiscourseUrl(uri))
            {
                try
                {
                    return await FetchDiscourseThread(uri, url);
                }
                catch (Exception ex)
                {
                    InternalLog.LogWarning(
                        $"[WebFetchTools] Discourse JSON fetch failed, falling back to HTML: {ex.Message}");
                }
            }

            try
            {
                // Stream the response to avoid buffering huge files (e.g., binaries) into RAM.
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd(GetUserAgent(uri));
                using var response = await s_HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }

                string htmlContent;
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var buffer = new char[k_MaxContentSizePerUrl];
                    int totalRead = 0;
                    int charsRead;
                    while (totalRead < buffer.Length &&
                        (charsRead = await reader.ReadAsync(buffer, totalRead, buffer.Length - totalRead)) > 0)
                    {
                        totalRead += charsRead;
                    }
                    htmlContent = new string(buffer, 0, totalRead);
                }

                var title = ExtractTitle(htmlContent, url);
                // Convert full HTML to markdown first so script/style tags are properly
                // stripped, then truncate the clean markdown to the size limit.
                var markdownContent = ConvertHtmlToMarkdown(htmlContent);
                if (markdownContent.Length > k_MaxContentSizePerUrl)
                {
                    markdownContent = markdownContent.Substring(0, k_MaxContentSizePerUrl);
                }

                return new FetchedPage
                {
                    Url = url,
                    Title = title,
                    Content = markdownContent
                };
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException($"Request timeout after {k_TimeoutSeconds} seconds");
            }
        }

        // Discourse (discussions.unity.com, forum.unity.com) renders page content via
        // JavaScript on the client side, so a plain HTTP GET returns an empty HTML shell
        // with no post text. Their public JSON API (appending .json to any topic URL)
        // returns structured post data directly, bypassing the JS rendering requirement.
        // If more site-specific handlers are needed in the future, consider extracting
        // an ISiteContentExtractor interface.

        static readonly string[] k_DiscourseHosts =
        {
            "discussions.unity.com",
            "forum.unity.com",
        };

        const int k_MaxDiscoursePostsToExtract = 10;

        static bool IsDiscourseUrl(Uri uri)
        {
            return Array.Exists(k_DiscourseHosts,
                host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase));
        }

        static async Task<FetchedPage> FetchDiscourseThread(Uri uri, string originalUrl)
        {
            var jsonPath = uri.AbsolutePath.TrimEnd('/') + ".json";
            var jsonUri = new UriBuilder(uri) { Path = jsonPath }.Uri;

            InternalLog.Log($"[WebFetchTools] Fetching Discourse JSON: {jsonUri}");

            using var request = new HttpRequestMessage(HttpMethod.Get, jsonUri);
            request.Headers.UserAgent.ParseAdd(GetUserAgent(jsonUri));
            using var response = await s_HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Discourse JSON API returned {(int)response.StatusCode}");

            string jsonContent;
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var buffer = new char[k_MaxContentSizePerUrl];
                int totalRead = 0;
                int charsRead;
                while (totalRead < buffer.Length &&
                    (charsRead = await reader.ReadAsync(buffer, totalRead, buffer.Length - totalRead)) > 0)
                {
                    totalRead += charsRead;
                }
                jsonContent = new string(buffer, 0, totalRead);
            }
            var topic = JObject.Parse(jsonContent);

            var title = topic["title"]?.ToString() ?? originalUrl;
            var posts = topic["post_stream"]?["posts"] as JArray;

            if (posts == null || posts.Count == 0)
                throw new Exception("No posts found in Discourse topic JSON");

            var content = new StringBuilder();
            var postsToExtract = Math.Min(posts.Count, k_MaxDiscoursePostsToExtract);

            for (int i = 0; i < postsToExtract; i++)
            {
                var post = posts[i];
                var username = post["username"]?.ToString() ?? "unknown";
                var createdAt = post["created_at"]?.ToString() ?? "";
                var cooked = post["cooked"]?.ToString() ?? "";

                var dateStr = createdAt;
                if (DateTime.TryParse(createdAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                    dateStr = parsedDate.ToString("yyyy-MM-dd");

                content.AppendLine($"### Post by {username} ({dateStr})");
                content.AppendLine(ConvertHtmlToMarkdown(cooked));
                content.AppendLine();
            }

            if (posts.Count > postsToExtract)
                content.AppendLine($"... ({posts.Count - postsToExtract} more replies not shown)");

            return new FetchedPage
            {
                Url = originalUrl,
                Title = title,
                Content = content.ToString()
            };
        }

        static string ExtractTitle(string html, string fallbackUrl)
        {
            // Try to extract title from <title> tag
            var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (titleMatch.Success)
            {
                var title = titleMatch.Groups[1].Value;
                // Decode HTML entities
                title = System.Net.WebUtility.HtmlDecode(title);
                return title.Trim();
            }

            // Fallback to URL
            return fallbackUrl;
        }

        static string ConvertHtmlToMarkdown(string html)
        {
            // Remove script and style blocks. The second pattern in each pair catches
            // unclosed tags left by stream truncation (opening tag with no closing tag).
            html = Regex.Replace(html, @"<script[^>]*>.*?</script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<script[^>]*>.*$", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<style[^>]*>.*?</style>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<style[^>]*>.*$", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Remove comments
            html = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);

            // Convert headings
            html = Regex.Replace(html, @"<h1[^>]*>(.*?)</h1>", "# $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<h2[^>]*>(.*?)</h2>", "## $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<h3[^>]*>(.*?)</h3>", "### $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<h4[^>]*>(.*?)</h4>", "#### $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<h5[^>]*>(.*?)</h5>", "##### $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<h6[^>]*>(.*?)</h6>", "###### $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Convert paragraphs
            html = Regex.Replace(html, @"<p[^>]*>(.*?)</p>", "$1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Convert line breaks
            html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

            // Convert lists
            html = Regex.Replace(html, @"<li[^>]*>(.*?)</li>", "- $1\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Convert code blocks
            html = Regex.Replace(html, @"<code[^>]*>(.*?)</code>", "`$1`", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<pre[^>]*>(.*?)</pre>", "```\n$1\n```\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Convert bold/strong
            html = Regex.Replace(html, @"<(b|strong)[^>]*>(.*?)</\1>", "**$2**", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Convert italic/em
            html = Regex.Replace(html, @"<(i|em)[^>]*>(.*?)</\1>", "*$2*", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Remove all remaining HTML tags
            html = Regex.Replace(html, @"<[^>]+>", "", RegexOptions.Singleline);

            // Decode HTML entities
            html = System.Net.WebUtility.HtmlDecode(html);

            html = Regex.Replace(html, @"\n{3,}", "\n\n");

            return html.Trim();
        }

    }
}
