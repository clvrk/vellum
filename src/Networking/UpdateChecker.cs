using System;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Net;
using Newtonsoft.Json;

namespace Vellum.Networking
{
    internal enum ReleaseProvider
    {
        GITHUB_RELEASES,
        HTML
    }

    internal enum VersionFormatting
    {
        MAJOR_MINOR_REVISION,
        MAJOR_MINOR_REVISION_BUILD,
        MAJOR_MINOR_BUILD_REVISION
    }

    internal class UpdateChecker
    {
        public Version RemoteVersion { get; private set; } = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        public ReleaseProvider Provider { get; private set; }
        private const ushort _timeout = 3000;
        private string _apiUrl;
        private string _regex;

        public UpdateChecker(ReleaseProvider provider, string apiUrl, string regex)
        {
            Provider = provider;
            _apiUrl = apiUrl;
            _regex = regex;
        }

        public bool GetLatestVersion()
        {
            bool result = false;

            HttpWebRequest req = WebRequest.CreateHttp(_apiUrl);
            req.Timeout = _timeout;
            req.UserAgent = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name.ToString();
            
            switch (Provider)
            {
                case ReleaseProvider.GITHUB_RELEASES:
                    try
                    {
                        HttpWebResponse resp = (HttpWebResponse)req.GetResponse();

                        using (StreamReader streamReader = new StreamReader(resp.GetResponseStream()))
                        {
                            string versionTag = (string)JsonConvert.DeserializeObject<dynamic>(streamReader.ReadToEnd())["tag_name"];
                            Match versionMatch = Regex.Match(versionTag, _regex);

                            if (versionMatch.Groups.Count >= 3)
                            {
                                RemoteVersion = new Version(Convert.ToInt32(versionMatch.Groups[1].Value), Convert.ToInt32(versionMatch.Groups[2].Value), 0, Convert.ToInt32(versionMatch.Groups[3].Value));
                                result = true;
                            }
                        }

                        resp.Close();
                    } catch
                    {
                        result = false;
                    }
                break;

                case ReleaseProvider.HTML:
                    //HttpWebRequest request = WebRequest.CreateHttp(_apiUrl);
                    try
                    {
                        HttpWebResponse resp = (HttpWebResponse)req.GetResponse();

                        using (StreamReader reader = new StreamReader(resp.GetResponseStream()))
                        {
                            Match match = Regex.Match(reader.ReadToEnd(), _regex);
                            
                            if (match.Groups.Count > 1)
                            {
                                RemoteVersion = UpdateChecker.ParseVersion(match.Groups[1].Value, VersionFormatting.MAJOR_MINOR_REVISION_BUILD);
                                result = true;
                            } else
                            {
                                result = false;
                            }
                        }
                    } catch
                    {
                        result = false;
                    }
                break;
            }

            return result;
        }

        public static string ParseVersion(Version version, VersionFormatting formatting)
        {
            StringBuilder builder = new StringBuilder();

            switch (formatting)
            {
                case VersionFormatting.MAJOR_MINOR_REVISION:
                    builder.Append(String.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Revision));
                break;

                case VersionFormatting.MAJOR_MINOR_REVISION_BUILD:
                    builder.Append(String.Format("{0}.{1}.{2}.{3}", version.Major, version.Minor, version.Revision, version.Build));
                break;

                case VersionFormatting.MAJOR_MINOR_BUILD_REVISION:
                    builder.Append(version.ToString());
                break;
            }

            return builder.ToString();
        }

        public static Version ParseVersion(string version, VersionFormatting formatting)
        {
            Version formattedVersion = new Version();
            MatchCollection matches = Regex.Matches(version, @"(\d+)");

            bool result = false;

            switch (formatting)
            {
                case VersionFormatting.MAJOR_MINOR_REVISION:
                    if (matches.Count == 3)
                    {
                        formattedVersion = new Version(Convert.ToInt32(matches[0].Captures[0].Value), Convert.ToInt32(matches[1].Captures[0].Value), 0, Convert.ToInt32(matches[2].Captures[0].Value));
                        result = true;
                    }
                break;

                case VersionFormatting.MAJOR_MINOR_REVISION_BUILD:
                    if (matches.Count == 4)
                    {
                        formattedVersion = new Version(Convert.ToInt32(matches[0].Captures[0].Value), Convert.ToInt32(matches[1].Captures[0].Value), Convert.ToInt32(matches[3].Captures[0].Value), Convert.ToInt32(matches[2].Captures[0].Value));
                        result = true;
                    }
                break;

                case VersionFormatting.MAJOR_MINOR_BUILD_REVISION:
                break;
            }

            if (!result)
            {
                throw new ArgumentException(String.Format("\"{0}\" could not be parsed into \"{1}\" format.", version, Enum.GetName(typeof(VersionFormatting), formatting)));
            }

            return formattedVersion;
        }
    }
}