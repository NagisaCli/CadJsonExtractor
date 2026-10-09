using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CadJsonExtractor.Core.Contract;
using CadJsonExtractor.Core.Serialization;

namespace CadJsonExtractor.AutoCAD.Export
{
    /// <summary>
    /// Handles clipboard publishing and JSON file export.
    /// </summary>
    public static class ClipboardBridge
    {
        private const string TempPrefix = "CadJson-";

        public sealed class PublishResult
        {
            public bool Success;
            public bool UsedFileFallback;
            public string FilePath;
            public int JsonBytes;
            public string Error;
        }

        public static PublishResult Publish(CadJsonPayload payload, ValidationLimits limits = null)
        {
            limits = limits ?? new ValidationLimits();
            var res = new PublishResult();

            string json;
            try { json = PayloadSerializer.Serialize(payload, indented: false); }
            catch (Exception ex) { res.Error = "Serialization failed: " + ex.Message; return res; }

            res.JsonBytes = Encoding.UTF8.GetByteCount(json);

            try
            {
                if (res.JsonBytes <= limits.MaxClipboardBytes)
                {
                    var data = new DataObject();
                    data.SetData(SchemaConstants.ClipboardFormat, json);
                    data.SetData(SchemaConstants.LegacyClipboardFormat, json);
                    data.SetData(DataFormats.UnicodeText, json);
                    SetClipboardWithRetry(data);
                    res.Success = true;
                    return res;
                }

                // Large payload: write a temp file and publish an envelope
                string dir = Path.Combine(Path.GetTempPath(), "CadJsonExtractor");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir,
                    TempPrefix + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                    Guid.NewGuid().ToString("N").Substring(0, 8) + SchemaConstants.FileExtension);

                File.WriteAllText(file, json, new UTF8Encoding(false));

                var env = new PayloadFileEnvelope
                {
                    FilePath = file,
                    FileSize = new FileInfo(file).Length,
                    Sha256 = PayloadSerializer.Sha256HexOfFile(file)
                };
                string envJson = PayloadSerializer.SerializeEnvelope(env);

                var envData = new DataObject();
                envData.SetData(SchemaConstants.ClipboardFormat, envJson);
                envData.SetData(SchemaConstants.LegacyClipboardFormat, envJson);
                envData.SetData(DataFormats.UnicodeText, envJson);
                SetClipboardWithRetry(envData);

                res.Success = true;
                res.UsedFileFallback = true;
                res.FilePath = file;
                return res;
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
                return res;
            }
        }

        private static void SetClipboardWithRetry(DataObject data, int attempts = 5)
        {
            Exception last = null;
            for (int i = 0; i < attempts; i++)
            {
                try { Clipboard.SetDataObject(data, true); return; }
                catch (Exception ex) { last = ex; Thread.Sleep(80); }
            }
            throw new InvalidOperationException(
                "Could not open the Windows clipboard after several attempts. " +
                "Another application may be holding it. " + (last?.Message ?? ""), last);
        }

        public static string ExportToFile(CadJsonPayload payload, string path, bool indented = true)
        {
            string json = PayloadSerializer.Serialize(payload, indented: indented);
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return path;
        }

        public static int CleanupTempFiles(TimeSpan olderThan)
        {
            int removed = 0;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "CadJsonExtractor");
                if (!Directory.Exists(dir)) return 0;
                DateTime cutoff = DateTime.UtcNow - olderThan;

                foreach (string f in Directory.GetFiles(dir, TempPrefix + "*" + SchemaConstants.FileExtension))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(f) < cutoff) { File.Delete(f); removed++; }
                    }
                    catch { }
                }
            }
            catch { }
            return removed;
        }
    }
}
