using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AlpineTuning
{
    internal static class GarageConnectionStorage
    {
        internal static bool TryNormalizeEndpoint(string value, out string endpoint, out string reason)
        {
            endpoint = null;
            reason = "Use an HTTPS Garage address, or HTTP on localhost for development.";
            if (!Uri.TryCreate((value ?? string.Empty).Trim(), UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
                return false;
            endpoint = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            reason = null;
            return true;
        }

        internal static GarageConnectionRecord Load(string path, out string reason)
        {
            reason = null;
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    var record = JsonConvert.DeserializeObject<GarageConnectionRecord>(File.ReadAllText(candidate));
                    if (record == null || !TryNormalizeEndpoint(record.apiBaseUrl, out string endpoint, out _))
                        throw new InvalidDataException();
                    record.apiBaseUrl = endpoint;
                    record.processedImportActionIds = record.processedImportActionIds ?? new List<string>();
                    // Old paired installations can resume. A recovery or an
                    // unfinished pairing always needs an explicit reconnect.
                    record.enabled = candidate == path && !string.IsNullOrWhiteSpace(record.installationToken) &&
                        (record.enabled ?? true);
                    if (endpoint == "http://127.0.0.1:3001" || endpoint == "http://localhost:3001")
                    {
                        record.apiBaseUrl = GarageConnectionRecord.ProductionApiBaseUrl;
                        record.enabled = false;
                        record.installationId = record.installationToken = record.linkedUser = null;
                        record.processedImportActionIds.Clear();
                        record.pendingCompletion = null;
                        reason = "Garage address updated. Choose Connect to pair with Alpine Garage.";
                    }
                    if (candidate != path)
                    {
                        reason = "Recovered Garage connection backup; reconnect to resume.";
                        // Keep the known-good backup when repairing a damaged primary.
                        if (!TrySave(path, record, out string repairError, false))
                            reason += " " + repairError;
                    }
                    return record;
                }
                catch (Exception ex)
                {
                    reason = "Garage connection could not be read: " + ex.GetType().Name;
                }
            }
            return new GarageConnectionRecord { enabled = false };
        }

        internal static bool TrySave(string path, GarageConnectionRecord record, out string reason, bool keepBackup = true)
        {
            reason = null;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(record, Formatting.Indented));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path))
                    File.Replace(temporary, path, keepBackup ? path + ".bak" : null, true);
                else
                    File.Move(temporary, path);
                return true;
            }
            catch (Exception ex)
            {
                reason = "Garage connection could not be saved: " + ex.GetType().Name;
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        internal static string ReadResponse(Stream stream)
        {
            const int maximumCharacters = 2 * 1024 * 1024;
            using (var reader = new StreamReader(stream))
            {
                var result = new StringBuilder();
                var buffer = new char[4096];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (result.Length + read > maximumCharacters)
                        throw new InvalidDataException("Garage response exceeds the supported size.");
                    result.Append(buffer, 0, read);
                }
                return result.ToString();
            }
        }
    }
}
