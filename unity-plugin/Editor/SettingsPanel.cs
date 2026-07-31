using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityAI.Lib;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityAI
{
    /// <summary>
    /// AYARLAR SEKMESİ — TEK ALAN: anahtarı yapıştır, "Bağla"ya bas. Bitti.
    ///
    /// TASARIM GEÇMİŞİ (aynı hatalara dönmemek için):
    ///  1) 8 sağlayıcı × ayrı alan/buton  → 32 satırlık duvar, butonlar sarıyordu.
    ///  2) Kullanıcı model adını ELLE yazıyordu → bir harf hata = sessiz 404.
    ///  3) Model listesi çekilip kullanıcıya SEÇTİRİLİYORDU → kullanıcı hangi modelin
    ///     araç çağırabildiğini bilemez. Sahada iki gerçek hata çıktı:
    ///       "`tool calling` is not supported with this model"
    ///       "413 Request too large … TPM Limit 6000"
    ///  4) BU SÜRÜM: hiç seçim yok. Backend anahtarı tanır, model listesini çeker,
    ///     her adayı GERÇEK bir araç çağrısıyla dener ve çalışan ilkini kendisi seçer.
    ///
    /// GÜVENLİK: anahtar backend'e gider, kullanıcının diskinde şifreli saklanır;
    /// Unity tarafında hiçbir yere yazılmaz, kaydedilince alan temizlenir.
    /// </summary>
    internal sealed class SettingsPanel
    {
        // Otomatik kurulum model deneyebildiği için uzun sürebilir.
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        private readonly Action<string> _status;

        private TextField _keyField, _customBase;
        private DropdownField _presetDd;
        private Label _statusLabel, _detail, _active, _mode;
        private VisualElement _savedList;

        private sealed class SavedKey { public string Provider, Label, Hint; }
        private readonly List<SavedKey> _saved = new List<SavedKey>();

        /// <summary>Hazır sunucu adresleri: (etiket, adres). Backend kataloğundan gelir.</summary>
        private readonly List<KeyValuePair<string, string>> _presets = new List<KeyValuePair<string, string>>();

        private bool _busy;

        public SettingsPanel(VisualElement root)
        {
            _statusLabel = root.Q<Label>("set_status");
            _status = m => { if (_statusLabel != null) _statusLabel.text = m; };

            _keyField = root.Q<TextField>("set_key");
            _customBase = root.Q<TextField>("custom_base");
            _presetDd = root.Q<DropdownField>("set_preset");
            if (_presetDd != null)
                _presetDd.RegisterValueChangedCallback(_ =>
                {
                    // Hazır servis seçilince adresi otomatik doldur; "Elle yazacağım"da boşalt.
                    int i = _presetDd.index;
                    if (_customBase == null) return;
                    _customBase.SetValueWithoutNotify(i > 0 && i - 1 < _presets.Count ? _presets[i - 1].Value : "");
                });
            _detail = root.Q<Label>("set_detail");
            _active = root.Q<Label>("set_active");
            _mode = root.Q<Label>("set_mode");
            _savedList = root.Q<VisualElement>("set_saved_list");

            var connect = root.Q<Button>("set_key_save");
            if (connect != null) connect.clicked += () => _ = Connect();
            var test = root.Q<Button>("set_test");
            if (test != null) test.clicked += () => _ = TestConnection();
            var reload = root.Q<Button>("set_reload");
            if (reload != null) reload.clicked += () => _ = Reload();
        }

        // ------------------------------------------------------------------ HTTP

        private static string Url(string p) => UnityAIConfig.BaseUrl.TrimEnd('/') + p;

        private static HttpRequestMessage Req(HttpMethod m, string path, object body = null)
        {
            var r = new HttpRequestMessage(m, Url(path));
            r.Headers.Add("Authorization", "Bearer " + UnityAIConfig.ApiToken);
            if (body != null) r.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
            return r;
        }

        private static string Str(Dictionary<string, object> d, string k) =>
            d != null && d.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
        private static bool Bl(Dictionary<string, object> d, string k) =>
            d != null && d.TryGetValue(k, out var v) && v is bool b && b;

        private static string ErrOf(string raw)
        {
            try
            {
                if (Json.Deserialize(raw) is Dictionary<string, object> d && d.TryGetValue("error", out var e))
                    return e?.ToString() ?? raw;
            }
            catch { }
            return string.IsNullOrEmpty(raw) ? "bilinmeyen hata" : raw;
        }

        // ------------------------------------------------------------------ durum

        public async Task Reload()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                using var resp = await Http.SendAsync(Req(HttpMethod.Get, "/v1/settings"));
                string txt = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) { _status(NovaLocale.T("set.loadFail", ErrOf(txt))); return; }
                if (!(Json.Deserialize(txt) is Dictionary<string, object> d))
                { _status(NovaLocale.T("set.loadFail", "geçersiz yanıt")); return; }

                // Backend eski sürümse yeni uçlar yok — DOĞRU sebebi söyle.
                if (!d.ContainsKey("services")) { _status(NovaLocale.T("set.oldBackend")); return; }

                _saved.Clear();
                if (d.TryGetValue("savedKeys", out var kv) && kv is List<object> kl)
                    foreach (var o in kl)
                        if (o is Dictionary<string, object> kd)
                            _saved.Add(new SavedKey { Provider = Str(kd, "provider"), Label = Str(kd, "label"), Hint = Str(kd, "hint") });

                // Hazır adres listesi: kataloğun OpenAI-uyumlu (isCustom) ve adresi olan servisleri
                _presets.Clear();
                if (d.TryGetValue("services", out var sv2) && sv2 is List<object> sl2)
                    foreach (var o in sl2)
                        if (o is Dictionary<string, object> sd)
                        {
                            string url = Str(sd, "baseUrl");
                            if (Bl(sd, "isCustom") && !string.IsNullOrEmpty(url))
                                _presets.Add(new KeyValuePair<string, string>(Str(sd, "label"), url));
                        }
                if (_presetDd != null)
                {
                    var ch = new List<string> { NovaLocale.T("set.preset.manual") };
                    foreach (var kv2 in _presets) ch.Add(kv2.Key);
                    int keepP = Mathf.Clamp(_presetDd.index, 0, ch.Count - 1);
                    _presetDd.choices = ch;
                    _presetDd.index = keepP < 0 ? 0 : keepP;
                }

                if (_customBase != null) _customBase.SetValueWithoutNotify(Str(d, "customBaseUrl"));

                if (_active != null)
                {
                    string brain = "";
                    if (d.TryGetValue("roles", out var rv) && rv is List<object> rl)
                        foreach (var o in rl)
                            if (o is Dictionary<string, object> rd && Str(rd, "id") == "brain")
                                brain = Str(rd, "effective");
                    _active.text = _saved.Count == 0
                        ? NovaLocale.T("set.active.none")
                        : NovaLocale.T("set.active", brain);
                }

                bool pool = Bl(d, "poolMode");
                if (_mode != null)
                {
                    _mode.text = pool ? NovaLocale.T("set.mode.pool") : "";
                    _mode.style.display = pool ? DisplayStyle.Flex : DisplayStyle.None;
                }

                RebuildSavedList();
            }
            catch (Exception e) { _status(NovaLocale.T("set.serverDown", e.Message)); }
            finally { _busy = false; }
        }

        private void RebuildSavedList()
        {
            if (_savedList == null) return;
            _savedList.Clear();
            foreach (var p in _saved)
            {
                var row = new VisualElement();
                row.AddToClassList("set-saved");
                var name = new Label($"{p.Label}  ·  {p.Hint}");
                name.AddToClassList("set-saved-name");
                row.Add(name);
                string prov = p.Provider, label = p.Label;
                var del = new Button(() => _ = DeleteKey(prov, label)) { text = NovaLocale.T("set.btn.delKey") };
                del.AddToClassList("ghost-btn");
                row.Add(del);
                _savedList.Add(row);
            }
        }

        // ------------------------------------------------------------------ BAĞLA (otomatik)

        /// <summary>
        /// Kullanıcı yalnız anahtarı verir. Backend sağlayıcıyı tanır, modeli seçer.
        /// Kullanıcının model/servis bilgisi olması GEREKMEZ.
        /// </summary>
        private async Task Connect()
        {
            if (_busy) return;
            string key = _keyField?.value?.Trim() ?? "";
            if (key.Length < 8) { _status(NovaLocale.T("set.key.tooShort")); return; }

            string baseUrl = _customBase?.value?.Trim() ?? "";
            if (!string.IsNullOrEmpty(baseUrl) && !baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            { _status(NovaLocale.T("set.custom.needUrl")); return; }

            _busy = true;
            _status(NovaLocale.T("set.detecting"));
            if (_detail != null) _detail.text = "";
            try
            {
                var body = new Dictionary<string, object> { { "apiKey", key } };
                if (!string.IsNullOrEmpty(baseUrl)) body["baseUrl"] = baseUrl;

                using var resp = await Http.SendAsync(Req(HttpMethod.Post, "/v1/settings/auto", body));
                string txt = await resp.Content.ReadAsStringAsync();
                var d = Json.Deserialize(txt) as Dictionary<string, object>;

                if (!resp.IsSuccessStatusCode || !Bl(d, "ok"))
                {
                    _status(NovaLocale.T("set.autoFail", d != null ? Str(d, "error") : ErrOf(txt)));
                    ShowRejected(d);
                    return;
                }

                _keyField?.SetValueWithoutNotify("");   // GÜVENLİK: alanı boşalt
                _status(NovaLocale.T("set.autoOk", Str(d, "label"), Str(d, "model")));
                ShowRejected(d);
            }
            catch (Exception e) { _status(NovaLocale.T("set.serverDown", e.Message)); return; }
            finally { _busy = false; }

            await Reload();
        }

        /// <summary>Denenip elenen modelleri sebepleriyle gösterir (teşhis).</summary>
        private void ShowRejected(Dictionary<string, object> d)
        {
            if (_detail == null) return;
            var lines = new List<string>();
            if (d != null && d.TryGetValue("rejected", out var rv) && rv is List<object> rl)
                foreach (var o in rl)
                    if (o is Dictionary<string, object> rd)
                        lines.Add($"• {Str(rd, "model")} — {Str(rd, "reason")}");
            _detail.text = lines.Count == 0 ? "" : NovaLocale.T("set.rejected") + "\n" + string.Join("\n", lines);
        }

        private async Task DeleteKey(string provider, string label)
        {
            if (_busy) return;
            if (!EditorUtility.DisplayDialog(NovaLocale.T("set.del.title"),
                    NovaLocale.T("set.del.body", label),
                    NovaLocale.T("set.btn.delKey"), NovaLocale.T("dialog.cancel")))
                return;

            _busy = true;
            try
            {
                using var resp = await Http.SendAsync(Req(HttpMethod.Delete, "/v1/keys/" + provider));
                string txt = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) { _status(NovaLocale.T("set.key.delFail", ErrOf(txt))); return; }
                _status(NovaLocale.T("set.key.deleted", label));
                if (_detail != null) _detail.text = "";
            }
            catch (Exception e) { _status(NovaLocale.T("set.serverDown", e.Message)); return; }
            finally { _busy = false; }

            await Reload();
        }

        // ------------------------------------------------------------------ test

        private async Task TestConnection()
        {
            if (_busy) return;
            _busy = true;
            _status(NovaLocale.T("set.test.running"));
            try
            {
                using var resp = await Http.SendAsync(Req(HttpMethod.Post, "/v1/settings/test",
                    new Dictionary<string, object> { { "role", "brain" } }));
                string txt = await resp.Content.ReadAsStringAsync();
                var d = Json.Deserialize(txt) as Dictionary<string, object>;
                if (resp.IsSuccessStatusCode && Bl(d, "ok"))
                    _status(NovaLocale.T("set.test.ok", Str(d, "provider"), Str(d, "model")));
                else
                    _status(NovaLocale.T("set.test.fail", d != null ? Str(d, "error") : ErrOf(txt)));
            }
            catch (Exception e) { _status(NovaLocale.T("set.serverDown", e.Message)); }
            finally { _busy = false; }
        }
    }
}
