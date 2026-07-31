using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityAI.Lib;
using UnityAI.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityAI
{
    /// <summary>
    /// Nova · UnityAI ana penceresi. Sekmeli düzen: Sohbet (agent) | Kod | 3D Stüdyo.
    /// Agent döngüsü: kullanıcı -> LLM -> tool_call -> Unity -> tool_result -> ... -> bitiş.
    /// </summary>
    public class UnityAIWindow : EditorWindow, IHasCustomMenu
    {
        private string _baseUrl;
        private const int MaxTurns = 12;

        // Tek beyin: .env'deki GROQ_API_KEY ile çalışan Nova beyni.
        // Sahte (mock) model ve anahtarı olmayan sağlayıcılar UI'dan kaldırıldı —
        // kullanıcıya asla önceden yazılmış cevap gösterilmez.
        private const string ActiveModel = "nova-flash";

        // Kod ajanı paneli
        private ScrollView _messages;
        private TextField _input;
        private Label _status;
        private Label _cost;
        private Toggle _autoApprove;
        private Toggle _council;
        private DropdownField _model;      // artık UXML'de yok (null) — geriye dönük uyum
        private Button _submitBtn, _stopBtn;
        private Label _brainLabel;

        // Eklenen görseller (base64 PNG) — bir sonraki mesajla birlikte gider
        private VisualElement _attachStrip, _attachThumbs;

        /// <summary>Mesajla birlikte gidecek ek (görsel ya da belge). Her birinin kendi ✕ düğmesi var.</summary>
        private class Attachment
        {
            public bool IsDoc;
            public string Base64;        // görsel: PNG base64
            public string Name, Text;    // belge: ad + içerik
            public Texture2D Tex;
            public VisualElement Chip;
        }
        private readonly List<Attachment> _attachments = new List<Attachment>();

        // Modelin canlı düşünmesi + kullanıcıya sorduğu soru
        private Label _reasoningLabel;
        private Label _emptyHint;
        private StringBuilder _reasonText;

        // Sohbetten başlatılan 3D üretim: tur burada askıya alınır, model gelince devam eder
        private string _pendingModelCallId, _pendingModelName;
        private Label _modelProgressLabel;
        private double _modelStartedAt;
        private const double ModelTimeoutSecs = 240;
        private string _pendingAskId;      // AskUser çağrısı beklemedeyse tool_call id'si

        // Sekmeler + diğer paneller
        private VisualElement _panelChat, _panelStudio, _panelMaterial;
        private Button _tabChat, _tabStudio, _tabMaterial;
        private TextField _matPrompt;
        private Slider _matTiling;
        private Label _matStatus;
        private UnityEditor.UIElements.ObjectField _matTarget;
        private Button _tabWorld;
        private VisualElement _panelWorld;
        // Ayarlar sekmesi (API anahtarları + rol bazlı model seçimi)
        private VisualElement _panelSettings;
        private SettingsPanel _settings;
        private bool _settingsLoaded;
        private Slider _worldDensity;
        private Label _worldStatus, _worldTypeHint;
        private DropdownField _worldSky, _worldType, _worldSize;
        private Toggle _wTrees, _wBushes, _wRocks, _wRiver, _wLake, _wVehicles, _wProps, _wPlayer;
        // Arazi şekli + gelişmiş + atmosfer kontrolleri
        private Slider _worldRelief, _worldRiverCurve, _treeMul, _rockMul, _bushMul, _fogDens;
        private Toggle _wFlowers, _wPath, _wFog, _wWind;
        private TextField _worldSeed;
        private DropdownField _worldSun;
        // Oyun tipi seçici + gruplar
        private DropdownField _gameType;
        private VisualElement _grpOpenWorld, _grpRunner, _grpArena, _grpPlatformer, _grpRacer, _grpTd, _grp2d;

        // ---- Dünya sihirbazı: harita tipleri (deterministik reçete; LLM yok) ----
        private struct WType
        {
            public string Key, Name, Hint, Mode, Biome;
            public bool Trees, Bushes, Rocks, River, Lake, Vehicles, Props;
            public string L => NovaLocale.T("map." + Key);          // yerelleşmiş ad
            public string LH => NovaLocale.T("map." + Key + ".h");  // yerelleşmiş açıklama
        }

        // NOT: Şehir tipleri ASKIYA ALINDI (2026-07 pivot) — kod duruyor (BuildOrganic/CityLayout),
        // menüden kaldırıldı. Odak: arazi/biome üretimi. İleride "birkaç tıkla oyun" vizyonuyla dönebilir.
        private static readonly WType[] WTypes =
        {
            new WType { Key = "plains", Name = "Ova (çayır)", Mode = "terrain", Biome = "plains", Trees = true, Bushes = true },
            new WType { Key = "forest", Name = "Orman", Mode = "terrain", Biome = "forest", Trees = true, Bushes = true, Rocks = true },
            new WType { Key = "valley", Name = "Dağ Vadisi (ırmaklı)", Mode = "terrain", Biome = "valley", Trees = true, Bushes = true, Rocks = true, River = true },
            new WType { Key = "hills", Name = "Tepelik", Mode = "terrain", Biome = "hills", Trees = true, Bushes = true, Rocks = true },
            new WType { Key = "coast", Name = "Sahil", Mode = "terrain", Biome = "coast", Trees = true, Rocks = true },
            new WType { Key = "desert", Name = "Çöl", Mode = "terrain", Biome = "desert", Rocks = true },
            new WType { Key = "lakeside", Name = "Göl Kenarı", Mode = "terrain", Biome = "plains", Trees = true, Bushes = true, Lake = true },
            // Yeni biome'lar
            new WType { Key = "snow", Name = "Karlı Dağlar", Mode = "terrain", Biome = "snow", Trees = true, Rocks = true },
            new WType { Key = "swamp", Name = "Bataklık", Mode = "terrain", Biome = "swamp", Trees = true, Bushes = true },
            new WType { Key = "canyon", Name = "Kanyon / Mesa", Mode = "terrain", Biome = "canyon", Rocks = true },
            new WType { Key = "volcanic", Name = "Volkanik", Mode = "terrain", Biome = "volcanic", Rocks = true },
        };
        private VisualElement _codeSection;
        private ScrollView _codeList, _studioGallery;
        private TextField _studioPrompt, _studioImage;
        private Label _studioStatus;
        private IMGUIContainer _studioPreview;
        private Button _studioAdd, _studioClear;
        private ModelPreview _preview;
        private VisualElement _rootEl;
        private Button _themeBtn;
        private VisualElement _studioStats;
        private Button _studioRig;
        private bool _isRigged;
        private string _walkUrl;
        private double _lastSecs;
        private string _lastPrompt;
        private string[] _activeSteps;
        private DropdownField _studioMode;
        private DropdownField _studioQuality;
        private DropdownField _studioAnim;
        private static readonly int[] AnimIds = { 0, 30, 14, 86, 4, 90, 89, 87 };
        private DropdownField _studioHeight;
        private static readonly float[] HeightVals = { 1.8f, 1.0f, 0.5f, 3.0f };
        private DropdownField _studioSize;
        private static readonly float[] SizeVals = { 0f, 1.8f, 4f, 8f, 5f, 0.5f };
        private float _riggedH;
        private VisualElement _studioImageRow;
        private Button _studioImgUpload, _studioImgGen;
        private Image _studioImgPreview;
        private string _imgDataUri;
        private string _imgUrl;
        private static string[] RigSteps => new[]
        {
            NovaLocale.T("step.modelSent"), NovaLocale.T("step.rigging"),
            NovaLocale.T("step.unityImport"), NovaLocale.T("step.previewPrep"),
        };
        private bool _genActive, _tickOn;
        private int _genStep;
        private double _genStart;
        private GUIStyle _ovTitle, _ovStep;
        private static string[] GenSteps => new[]
        {
            NovaLocale.T("step.promptSent"), NovaLocale.T("step.modelBuilding"), NovaLocale.T("step.texturePrep"),
            NovaLocale.T("step.unityImport"), NovaLocale.T("step.previewPrep"),
        };

        private readonly List<BackendClient.Message> _history = new List<BackendClient.Message>();
        private readonly ConcurrentQueue<Dictionary<string, object>> _events =
            new ConcurrentQueue<Dictionary<string, object>>();

        private readonly List<PendingCall> _turnCalls = new List<PendingCall>();
        private StringBuilder _turnText;
        private Label _streamingLabel;
        private int _turnGuard;
        private double _totalCost;
        private bool _running;
        private bool _restoring;           // geri yükleme sırasında tekrar kaydetme
        private CancellationTokenSource _cts;

        private struct PendingCall
        {
            public string Id, Name, ArgsJson;
            public Dictionary<string, object> Args;
        }

        [MenuItem("Window/Nova · UnityAI")]
        [MenuItem("UnityAI/Nova penceresini aç/kapat %g")]
        public static void Toggle()
        {
            var w = GetWindow<UnityAIWindow>();
            w.titleContent = new GUIContent("Nova · UnityAI");
            w.minSize = new Vector2(360, 480);
            w.Show();
        }

        private void OnEnable()
        {
            _baseUrl = UnityAIConfig.BaseUrl; // EditorPrefs OnEnable'da okunmalı
            var root = rootVisualElement;
            var uxml = Resources.Load<VisualTreeAsset>("unityai");
            if (uxml != null) uxml.CloneTree(root);
            var uss = Resources.Load<StyleSheet>("style");
            if (uss != null) root.styleSheets.Add(uss);

            _messages = root.Q<ScrollView>("messages");
            _input = root.Q<TextField>("chat_input");
            _status = root.Q<Label>("status");
            _cost = root.Q<Label>("cost");
            _autoApprove = root.Q<Toggle>("auto_approve");
            _council = root.Q<Toggle>("council");
            _model = root.Q<DropdownField>("model");

            _panelChat = root.Q<VisualElement>("panel_chat");
            _panelStudio = root.Q<VisualElement>("panel_studio");
            _tabChat = root.Q<Button>("tab_chat_btn");
            _tabStudio = root.Q<Button>("tab_studio_btn");
            _panelMaterial = root.Q<VisualElement>("panel_material");
            _tabMaterial = root.Q<Button>("tab_material_btn");
            _matPrompt = root.Q<TextField>("mat_prompt");
            _matTiling = root.Q<Slider>("mat_tiling");
            _matStatus = root.Q<Label>("mat_status");
            _matTarget = root.Q<UnityEditor.UIElements.ObjectField>("mat_target");
            if (_matTarget != null) _matTarget.objectType = typeof(GameObject);
            _panelWorld = root.Q<VisualElement>("panel_world");
            _tabWorld = root.Q<Button>("tab_world_btn");
            _panelSettings = root.Q<VisualElement>("panel_settings");
            _worldDensity = root.Q<Slider>("world_density");
            _worldStatus = root.Q<Label>("world_status");
            _worldSky = root.Q<DropdownField>("world_sky");
            _worldType = root.Q<DropdownField>("world_type");
            _worldTypeHint = root.Q<Label>("world_type_hint");
            _worldSize = root.Q<DropdownField>("world_size");
            _wTrees = root.Q<Toggle>("world_opt_trees");
            _wBushes = root.Q<Toggle>("world_opt_bushes");
            _wRocks = root.Q<Toggle>("world_opt_rocks");
            _wRiver = root.Q<Toggle>("world_opt_river");
            _wLake = root.Q<Toggle>("world_opt_lake");
            _wVehicles = root.Q<Toggle>("world_opt_vehicles");
            _wProps = root.Q<Toggle>("world_opt_props");
            _wPlayer = root.Q<Toggle>("world_opt_player");

            // ---- Arazi şekli + gelişmiş + atmosfer ----
            _worldRelief = root.Q<Slider>("world_relief");
            _worldRiverCurve = root.Q<Slider>("world_rivercurve");
            _wFlowers = root.Q<Toggle>("world_opt_flowers");
            _wPath = root.Q<Toggle>("world_opt_path");
            _treeMul = root.Q<Slider>("world_treemul");
            _rockMul = root.Q<Slider>("world_rockmul");
            _bushMul = root.Q<Slider>("world_bushmul");
            _worldSeed = root.Q<TextField>("world_seed");
            var dice = root.Q<Button>("world_seed_dice");
            if (dice != null) dice.clicked += () =>
            { if (_worldSeed != null) _worldSeed.value = new System.Random().Next().ToString(); };

            _wFog = root.Q<Toggle>("world_fog");
            _wWind = root.Q<Toggle>("world_wind");
            _fogDens = root.Q<Slider>("world_fogdens");
            _worldSun = root.Q<DropdownField>("world_sun");
            if (_worldSun != null)
            {
                _worldSun.choices = new List<string>
                { NovaLocale.T("sun.auto"), NovaLocale.T("sun.morning"), NovaLocale.T("sun.noon"), NovaLocale.T("sun.evening") };
                _worldSun.index = 0;
            }
            // Atmosfer anında uygulanır — harita kurmayı beklemez (Undo gerektirmeyen sahne ayarları)
            _wFog?.RegisterValueChangedCallback(_ => ApplyAtmosphere());
            _fogDens?.RegisterValueChangedCallback(_ => ApplyAtmosphere());
            _worldSun?.RegisterValueChangedCallback(_ => ApplyAtmosphere());
            _wWind?.RegisterValueChangedCallback(_ => ApplyAtmosphere());
            if (_worldType != null)
            {
                _worldType.choices = WTypes.Select(t => t.L).ToList();
                _worldType.index = 0;
                _worldType.RegisterValueChangedCallback(_ => ApplyWorldType());
            }
            if (_worldSize != null)
            {
                _worldSize.choices = new List<string> { NovaLocale.T("size.small"), NovaLocale.T("size.medium"), NovaLocale.T("size.large") };
                _worldSize.index = 1;
            }

            // ---- DİL SEÇİMİ ----
            var langSel = root.Q<DropdownField>("lang_select");
            if (langSel != null)
            {
                langSel.choices = new List<string>(NovaLocale.LangNames);
                langSel.index = (int)NovaLocale.Current;
                langSel.RegisterValueChangedCallback(e =>
                {
                    int i = langSel.choices.IndexOf(e.newValue);
                    if (i >= 0 && i != (int)NovaLocale.Current)
                    {
                        NovaLocale.Current = (NovaLocale.Lang)i;
                        LocalizeUI(); // anında uygulanır, yeniden başlatma yok
                    }
                });
            }

            ApplyWorldType();
            LocalizeUI();
            if (_worldSky != null)
            {
                // Prosedürel presetler + indirilen gerçek HDRI gökyüzleri
                var skyChoices = new List<string>(SkyboxPresets.Names);
                foreach (var h in HdriSky.Available(true)) skyChoices.Add("🌤 " + h.Label);
                _worldSky.choices = skyChoices;
                _worldSky.index = 0;
                _worldSky.RegisterValueChangedCallback(_ => ApplySky());
            }
            _codeSection = root.Q<VisualElement>("code_section");
            _codeList = root.Q<ScrollView>("code_list");
            _studioGallery = root.Q<ScrollView>("studio_gallery");
            _studioPrompt = root.Q<TextField>("studio_prompt");
            _studioImage = root.Q<TextField>("studio_image");
            _studioStatus = root.Q<Label>("studio_status");

            _submitBtn = root.Q<Button>("submit");
            if (_submitBtn != null) _submitBtn.clicked += OnSubmit;
            _stopBtn = root.Q<Button>("stop");
            if (_stopBtn != null) _stopBtn.clicked += StopRun;
            _brainLabel = root.Q<Label>("brain_label");
            if (_brainLabel != null) _brainLabel.text = "Beyin: Nova (Groq)";

            // ---- Ek dosyalar (görsel/belge) ----
            _attachStrip = root.Q<VisualElement>("attach_strip");
            _attachThumbs = root.Q<VisualElement>("attach_thumbs");
            var attachClear = root.Q<Button>("attach_clear");
            if (attachClear != null) attachClear.clicked += ClearAttachments;

            var plus = root.Q<Button>("btn_plus");
            if (plus != null) plus.clicked += () => ShowPlusMenu(plus);
            var burger = root.Q<Button>("btn_menu");
            if (burger != null) burger.clicked += () => ShowToolsMenu(burger);

            // ---- Klavye: Enter gönderir, Shift+Enter alt satır, Ctrl+V görsel yapıştırır ----
            if (_input != null)
            {
                _input.RegisterCallback<KeyDownEvent>(e =>
                {
                    // Ctrl/Cmd + V: panoda GÖRSEL varsa onu ekle, yoksa normal metin yapıştırmasına izin ver
                    if ((e.ctrlKey || e.commandKey) && e.keyCode == KeyCode.V)
                    {
                        if (TryPasteImageFromClipboard())
                        {
                            e.StopPropagation();
                            _input.focusController?.IgnoreEvent(e);
                        }
                        return;
                    }

                    if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                    if (e.shiftKey) return;                 // Shift+Enter: yeni satır
                    // Unity 6: PreventDefault() kaldırıldı — StopPropagation + IgnoreEvent kullanılır.
                    e.StopPropagation();
                    _input.focusController?.IgnoreEvent(e);
                    // Metin alanı Enter'ı kendi işlemeden gönder
                    EditorApplication.delayCall += OnSubmit;
                }, TrickleDown.TrickleDown);
            }

            var qScan = root.Q<Button>("quick_scan");
            if (qScan != null) qScan.clicked += () => AppendMessage(NovaLocale.T("chat.role.auditor"), SceneHealth.ScanAndReport());
            var qFix = root.Q<Button>("quick_fix");
            if (qFix != null) qFix.clicked += () => RunPrompt("Konsoldaki derleme/hatalarını oku ve düzelt. Hatadaki dosyayı ReadScript ile oku, WriteScript ile minimal bir diff öner.");
            var qPlayer = root.Q<Button>("quick_player");
            if (qPlayer != null) qPlayer.clicked += () => RunPrompt("Bana bir 3D karakter kontrolcüsü (WASD hareket + zıplama, CharacterController tabanlı) C# script'i yaz. WriteScript ile Assets/Scripts/PlayerController.cs olarak öner ve nasıl kullanacağımı kısaca açıkla.");
            var qHealth = root.Q<Button>("quick_health");
            if (qHealth != null) qHealth.clicked += () => RunPrompt("Bir can (health) sistemi script'i yaz: maxHealth, currentHealth, TakeDamage, Heal ve ölüm eventi. WriteScript ile Assets/Scripts/Health.cs olarak öner.");
            var qInv = root.Q<Button>("quick_inventory");
            if (qInv != null) qInv.clicked += () => RunPrompt("Basit bir envanter sistemi script'i yaz: item ekle/çıkar/listele. WriteScript ile Assets/Scripts/Inventory.cs olarak öner.");

            var serverField = root.Q<TextField>("server_url");
            if (serverField != null)
            {
                serverField.value = _baseUrl;
                serverField.RegisterValueChangedCallback(e =>
                {
                    _baseUrl = string.IsNullOrEmpty(e.newValue) ? _baseUrl : e.newValue.TrimEnd('/');
                    UnityAIConfig.BaseUrl = _baseUrl;
                });
            }
            var newChat = root.Q<Button>("new_chat");
            if (newChat != null) newChat.clicked += NewChat;

            _rootEl = root.Q<VisualElement>(className: "root");
            _themeBtn = root.Q<Button>("theme_toggle");
            if (_themeBtn != null) _themeBtn.clicked += ToggleTheme;
            ApplyTheme(EditorPrefs.GetString("UnityAI.Theme", "dark"));

            if (_tabChat != null) _tabChat.clicked += () => SwitchTab("chat");
            if (_tabStudio != null) _tabStudio.clicked += () => SwitchTab("studio");
            if (_tabMaterial != null) _tabMaterial.clicked += () => SwitchTab("material");
            if (_tabWorld != null) _tabWorld.clicked += () => SwitchTab("world");
            if (_panelSettings != null) _settings = new SettingsPanel(root);
            var worldBuild = root.Q<Button>("world_build");
            if (worldBuild != null) worldBuild.clicked += BuildWorldFromControls;
            var worldExplore = root.Q<Button>("world_explore");
            if (worldExplore != null) worldExplore.clicked += OnWorldExplore;
            var worldSave = root.Q<Button>("world_save");
            if (worldSave != null) worldSave.clicked += () =>
                TerrainPersistence.Save(msg => { if (_worldStatus != null) _worldStatus.text = msg; });
            // NOT: AI görsel denetim TAMAMEN kaldırıldı (2026-07). Vision modeli JSON yerine
            // muhakeme metni döndürüyor, her kurulumda boşuna token harcıyordu. Sahne
            // doğrulaması artık yalnız deterministik SceneLint ile yapılıyor.
            // ---- OYUN TİPİ seçici: menüler seçime göre şekillenir ----
            _gameType = root.Q<DropdownField>("game_type");
            _grpOpenWorld = root.Q<VisualElement>("grp_open_world");
            _grpRunner = root.Q<VisualElement>("grp_runner");
            _grpArena = root.Q<VisualElement>("grp_arena");
            _grpPlatformer = root.Q<VisualElement>("grp_platformer");
            _grpRacer = root.Q<VisualElement>("grp_racer");
            _grpTd = root.Q<VisualElement>("grp_td");
            _grp2d = root.Q<VisualElement>("grp_2d");

            // TEŞHİS: UXML güncellenmediyse (Unity eski asset'i cache'lediyse) gruplar null olur
            // ve UI sessizce BOŞ görünür. Sessiz kalma — kullanıcıya ne yapacağını söyle.
            if (_grpArena == null || _grpPlatformer == null || _grpRunner == null)
                Debug.LogWarning("[Nova] UI şablonu (unityai.uxml) güncel değil — oyun tipi panelleri boş görünecek. " +
                    "Çözüm: Project penceresinde Packages > UnityAI > Editor/UI/Resources/unityai.uxml dosyasına " +
                    "sağ tık → Reimport, ya da Unity'yi kapatıp açın.");
            if (_gameType != null)
            {
                _gameType.choices = GameTypeChoices();
                _gameType.index = 0;
                _gameType.RegisterValueChangedCallback(_ => ApplyGameType());
            }
            ApplyGameType(); // başlangıç görünürlüğü + ipucu

            var gameArena = root.Q<Button>("game_arena");
            if (gameArena != null) gameArena.clicked += () =>
                ArenaBuilder.Build(msg => { if (_worldStatus != null) _worldStatus.text = msg; },
                    enterPlay: root.Q<Toggle>("arena_play")?.value ?? true);
            var gamePlat = root.Q<Button>("game_platformer");
            if (gamePlat != null) gamePlat.clicked += () =>
                PlatformerBuilder.Build(msg => { if (_worldStatus != null) _worldStatus.text = msg; },
                    enterPlay: root.Q<Toggle>("plat_play")?.value ?? true);
            var gameRacer = root.Q<Button>("game_racer");
            if (gameRacer != null) gameRacer.clicked += () =>
                RacerBuilder.Build(msg => { if (_worldStatus != null) _worldStatus.text = msg; },
                    enterPlay: root.Q<Toggle>("racer_play")?.value ?? true);
            var gameTd = root.Q<Button>("game_td");
            if (gameTd != null) gameTd.clicked += () =>
                TowerDefenseBuilder.Build(msg => { if (_worldStatus != null) _worldStatus.text = msg; },
                    enterPlay: root.Q<Toggle>("td_play")?.value ?? true);

            var gameRunner = root.Q<Button>("game_runner");
            if (gameRunner != null) gameRunner.clicked += () =>
            {
                bool play = root.Q<Toggle>("runner_play")?.value ?? true;
                RunnerBuilder.Build(msg => { if (_worldStatus != null) _worldStatus.text = msg; }, enterPlay: play);
            };
            var worldPrep = root.Q<Button>("world_prep");
            if (worldPrep != null) worldPrep.clicked += () =>
            {
                bool nav = root.Q<Toggle>("prep_navmesh")?.value ?? true;
                bool spw = root.Q<Toggle>("prep_spawn")?.value ?? true;
                bool mini = root.Q<Toggle>("prep_minimap")?.value ?? true;
                WorldPrep.PrepareForPlay(nav, spw, mini, msg => { if (_worldStatus != null) _worldStatus.text = msg; });
            };

            var matGen = root.Q<Button>("mat_generate");
            if (matGen != null) matGen.clicked += OnMaterialGenerate;
            var matRevert = root.Q<Button>("mat_revert");
            if (matRevert != null) matRevert.clicked += OnMaterialRevert;
            var studioGen = root.Q<Button>("studio_generate");
            if (studioGen != null) studioGen.clicked += OnStudioGenerate;

            _studioPreview = root.Q<IMGUIContainer>("studio_preview");
            _studioAdd = root.Q<Button>("studio_add");
            _studioClear = root.Q<Button>("studio_clear");
            _studioStats = root.Q<VisualElement>("studio_stats");
            _preview = new ModelPreview();
            if (_studioPreview != null)
                _studioPreview.onGUIHandler = () =>
                {
                    var r = new Rect(Vector2.zero, _studioPreview.contentRect.size);
                    _preview.OnGUI(r);
                    if (_genActive) DrawGenOverlay(r);
                    if (_preview.ConsumeDirty()) _studioPreview.MarkDirtyRepaint();
                };
            ShowStatsPlaceholder("Model üret → bilgiler burada");
            if (_studioAdd != null) { _studioAdd.clicked += OnStudioAdd; _studioAdd.SetEnabled(false); }
            if (_studioClear != null) _studioClear.clicked += OnStudioClear;

            _studioRig = root.Q<Button>("studio_rig");
            if (_studioRig != null) { _studioRig.clicked += OnStudioRig; _studioRig.SetEnabled(false); }

            _studioMode = root.Q<DropdownField>("studio_mode");
            _studioImageRow = root.Q<VisualElement>("studio_image_row");
            _studioImgUpload = root.Q<Button>("studio_img_upload");
            _studioImgGen = root.Q<Button>("studio_img_gen");
            _studioImgPreview = root.Q<Image>("studio_img_preview");
            if (_studioMode != null)
            {
                _studioMode.choices = new List<string> { "Metinden 3D", "Görselden 3D" };
                _studioMode.index = 0;
                _studioMode.RegisterValueChangedCallback(_ => UpdateStudioMode());
            }
            if (_studioImgUpload != null) _studioImgUpload.clicked += OnImageUpload;
            if (_studioImgGen != null) _studioImgGen.clicked += OnImageGenerate;
            _studioQuality = root.Q<DropdownField>("studio_quality");
            if (_studioQuality != null)
            {
                _studioQuality.choices = new List<string> { "Yüksek (varsayılan)", "Orta ~30k", "Düşük-poli ~12k (mobil)" };
                _studioQuality.index = 0;
            }
            _studioAnim = root.Q<DropdownField>("studio_anim");
            if (_studioAnim != null)
            {
                _studioAnim.choices = new List<string>
                { "Sadece rig (yürü/koş)", "Idle", "Yürüme", "Koşma", "Zıplama", "Saldırı", "Karşı saldırı", "Savaş duruşu", "Boks" };
                _studioAnim.index = 0;
            }
            _studioHeight = root.Q<DropdownField>("studio_height");
            if (_studioHeight != null)
            {
                _studioHeight.choices = new List<string> { "Boy: 1.8 m (insan)", "Boy: 1.0 m", "Boy: 0.5 m", "Boy: 3.0 m" };
                _studioHeight.index = 0;
            }
            _studioSize = root.Q<DropdownField>("studio_size");
            if (_studioSize != null)
            {
                _studioSize.choices = new List<string> { "Boyut: Otomatik", "İnsan ~1.8m", "Araba ~4m", "Ev ~8m", "Ağaç ~5m", "Küçük ~0.5m" };
                _studioSize.index = 0;
            }
            UpdateStudioMode();

            ModelGenerator.ModelGenerated += OnModelGenerated;
            CodeEdits.Changed += RebuildCodeList;
            CodeEdits.Proposed += OnEditProposed;

            SetStatus(NovaLocale.T("status.readyAt", _baseUrl));
            UpdateCost();
            EditorApplication.update += DrainEvents;

            // Boş sohbet ekranı bomboş görünmesin — tek satır, sessiz bir karşılama
            ShowEmptyHint();

            // Derleme (domain reload) sonrası konuşmayı geri yükle — sohbet artık silinmiyor.
            RestoreState();
        }

        private void ShowEmptyHint()
        {
            if (_messages == null || _emptyHint != null) return;
            _emptyHint = new Label(NovaLocale.T("chat.empty"));
            _emptyHint.AddToClassList("empty-hint");
            _messages.Add(_emptyHint);
        }

        private void HideEmptyHint()
        {
            if (_emptyHint == null) return;
            _emptyHint.RemoveFromHierarchy();
            _emptyHint = null;
        }

        /// <summary>Ekrandaki balonları + geçmişi SessionState'e yazar (derlemeye dayanıklı).</summary>
        private void SaveState()
        {
            if (_restoring || _messages == null) return;
            var view = new List<Dictionary<string, object>>();
            foreach (var child in _messages.Children())
            {
                var labels = child.Query<Label>().ToList();
                if (labels.Count < 2) continue;
                view.Add(new Dictionary<string, object> { { "s", labels[0].text }, { "b", labels[1].text } });
            }
            NovaChatState.Save(_history, view);
            NovaChatState.Cost = _totalCost;
        }

        private void RestoreState()
        {
            var view = NovaChatState.LoadView();
            var hist = NovaChatState.LoadHistory();
            if (view.Count == 0 && hist.Count == 0) return;

            _restoring = true;
            _history.Clear();
            _history.AddRange(hist);

            // Derleme, araç sonucu yazılmadan araya girdiyse geçmiş yarım kalır ve sağlayıcı
            // "tool_call'a karşılık gelen sonuç yok" diye reddeder. Boşlukları burada kapatıyoruz.
            var last = _history.Count > 0 ? _history[_history.Count - 1] : null;
            if (last != null && last.Role == "assistant" && last.ToolCalls != null)
                foreach (var tc in last.ToolCalls)
                    AddToolResult(tc.Id, false, NovaLocale.T("chat.msg.compileInterruptedResult"));
            foreach (var d in view)
            {
                string s = d.TryGetValue("s", out var sv) ? sv?.ToString() : "";
                string b = d.TryGetValue("b", out var bv) ? bv?.ToString() : "";
                AppendMessage(s, b);
            }
            _totalCost = NovaChatState.Cost;
            UpdateCost();
            _restoring = false;

            if (NovaChatState.WasInterrupted)
            {
                NovaChatState.WasInterrupted = false;
                AppendMessage(NovaLocale.T("chat.role.system"), NovaLocale.T("chat.msg.compileInterrupted"));
            }
            SetRunning(false, NovaLocale.T("status.ready"));
        }

        private void OnDisable()
        {
            EditorApplication.update -= DrainEvents;
            ModelGenerator.ModelGenerated -= OnModelGenerated;
            CodeEdits.Changed -= RebuildCodeList;
            CodeEdits.Proposed -= OnEditProposed;
            StopGenProgress();
            _preview?.Dispose();
            _cts?.Cancel();
        }

        /// <summary>
        /// Pencere sekmesindeki ÜÇ NOKTA menüsü.
        ///
        /// Ayarlar buraya taşındı: API anahtarı bir kez girilir, sonra bir daha
        /// açılmaz. Üstteki sekme çubuğunda sürekli yer kaplaması gereksizdi —
        /// çubuk zaten dar pencerede sarıyordu.
        /// </summary>
        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent(NovaLocale.T("menu.settings")), false, () => SwitchTab("settings"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(NovaLocale.T("menu.assetLib")), false,
                () => EditorApplication.ExecuteMenuItem("UnityAI/Asset Kütüphanesi…"));
        }

        private void SwitchTab(string tab)
        {
            if (_panelChat != null) _panelChat.style.display = tab == "chat" ? DisplayStyle.Flex : DisplayStyle.None;
            if (_panelStudio != null) _panelStudio.style.display = tab == "studio" ? DisplayStyle.Flex : DisplayStyle.None;
            if (_panelMaterial != null) _panelMaterial.style.display = tab == "material" ? DisplayStyle.Flex : DisplayStyle.None;
            if (_panelWorld != null) _panelWorld.style.display = tab == "world" ? DisplayStyle.Flex : DisplayStyle.None;
            if (_panelSettings != null) _panelSettings.style.display = tab == "settings" ? DisplayStyle.Flex : DisplayStyle.None;
            if (tab == "studio") ClearStudioFlag();   // "hazır" işaretini kaldır
            // Ayarlar ilk açılışta sunucudan okunur (her sekme geçişinde değil).
            if (tab == "settings" && _settings != null && !_settingsLoaded)
            {
                _settingsLoaded = true;
                _ = _settings.Reload();
            }
            SetActive(_tabChat, tab == "chat");
            SetActive(_tabStudio, tab == "studio");
            SetActive(_tabMaterial, tab == "material");
            SetActive(_tabWorld, tab == "world");
        }

        private void OnMaterialGenerate()
        {
            string pr = _matPrompt?.value?.Trim();
            if (string.IsNullOrEmpty(pr)) { if (_matStatus != null) _matStatus.text = "Malzeme açıklaması gir."; return; }
            float t = _matTiling != null ? _matTiling.value : 2f;

            // Hedef: sürüklenen nesne varsa o; yoksa sahnede seçili nesne(ler); hiçbiri yoksa düzlem.
            GameObject explicitTarget = _matTarget != null ? _matTarget.value as GameObject : null;
            GameObject[] targets = explicitTarget != null ? new[] { explicitTarget } : Selection.gameObjects;

            if (_matStatus != null) _matStatus.text = "Üretiliyor...";
            MaterialMaker.Generate(_baseUrl, pr, t, targets,
                msg => { if (_matStatus != null) _matStatus.text = msg; });
        }

        private void OnMaterialRevert()
        {
            MaterialMaker.Revert(msg => { if (_matStatus != null) _matStatus.text = msg; });
        }

        /// <summary>Tüm arayüz metinlerini seçili dile göre yazar (yeniden başlatma gerekmez).</summary>
        private void LocalizeUI()
        {
            var root = rootVisualElement;
            void B(string name, string key) { var b = root.Q<Button>(name); if (b != null) b.text = NovaLocale.T(key); }
            void L(string name, string key) { var l = root.Q<Label>(name); if (l != null) l.text = NovaLocale.T(key); }
            void Tg(string name, string key) { var t = root.Q<Toggle>(name); if (t != null) t.label = NovaLocale.T(key); }
            void Dd(string name, string key) { var d = root.Q<DropdownField>(name); if (d != null) d.label = NovaLocale.T(key); }

            L("lbl_tagline", "app.tagline");
            B("tab_chat_btn", "tab.chat"); B("tab_studio_btn", "tab.studio");
            B("tab_material_btn", "tab.material"); B("tab_world_btn", "tab.world");
            B("new_chat", "app.newchat");

            B("quick_scan", "chat.scan"); B("quick_fix", "chat.fix"); B("quick_player", "chat.player");
            B("quick_health", "chat.health"); B("quick_inventory", "chat.inventory");
            B("submit", "chat.send"); B("stop", "chat.stop");
            Tg("auto_approve", "chat.autoapprove"); Tg("council", "chat.council"); Dd("model", "chat.model");
            var input = root.Q<TextField>("chat_input");
            if (input != null) input.textEdition.placeholder = NovaLocale.T("chat.placeholder");

            Dd("world_type", "world.type");
            Tg("world_opt_trees", "world.trees"); Tg("world_opt_bushes", "world.bushes"); Tg("world_opt_rocks", "world.rocks");
            Tg("world_opt_river", "world.river"); Tg("world_opt_lake", "world.lake");
            Tg("world_opt_flowers", "world.flowers"); Tg("world_opt_path", "world.path");
            Tg("world_opt_player", "world.addplayer");
            Tg("world_fog", "world.fog"); Tg("world_wind", "world.wind");
            Dd("world_size", "world.size"); Dd("world_sky", "world.sky");
            void Sl(string name, string key) { var s = root.Q<Slider>(name); if (s != null) s.label = NovaLocale.T(key); }
            Sl("world_density", "world.density"); Sl("world_relief", "world.relief");
            Sl("world_rivercurve", "world.rivercurve"); Sl("world_treemul", "world.treemul");
            Sl("world_rockmul", "world.rockmul"); Sl("world_bushmul", "world.bushmul");
            Sl("world_fogdens", "world.fogdens");
            void Fo(string name, string key) { var f = root.Q<Foldout>(name); if (f != null) f.text = NovaLocale.T(key); }
            Fo("world_adv", "world.adv"); Fo("world_atmo", "world.atmo");
            var seedF = root.Q<TextField>("world_seed"); if (seedF != null) seedF.label = NovaLocale.T("world.seed");
            if (_worldSun != null)
            {
                int keepSun = _worldSun.index;
                _worldSun.label = NovaLocale.T("world.sun");
                _worldSun.choices = new List<string>
                { NovaLocale.T("sun.auto"), NovaLocale.T("sun.morning"), NovaLocale.T("sun.noon"), NovaLocale.T("sun.evening") };
                _worldSun.index = Mathf.Clamp(keepSun, 0, 3);
            }
            B("world_build", "world.build"); B("world_explore", "world.explore");
            B("world_save", "world.save"); B("world_prep", "world.prep");
            B("game_runner", "game.runner"); B("game_arena", "game.arena"); B("game_platformer", "game.platformer");
            Dd("game_type", "game.type");
            // Panel başlığı + oyun şablonu gruplarının açıklama/kontrol metinleri
            L("lbl_world_title", "world.title"); L("lbl_world_head_hint", "world.headhint");
            L("runner_desc", "runner.desc"); L("runner_controls", "runner.controls");
            L("arena_desc", "arena.desc"); L("arena_controls", "arena.controls");
            L("plat_desc", "plat.desc"); L("plat_controls", "plat.controls");
            L("racer_desc", "racer.desc"); L("racer_controls", "racer.controls");
            L("td_desc", "td.desc"); L("td_controls", "td.controls");
            L("twod_soon", "twod.soon");
            B("game_racer", "game.racer"); B("game_td", "game.td");
            Tg("runner_play", "game.playnow"); Tg("arena_play", "game.playnow"); Tg("plat_play", "game.playnow");
            Tg("racer_play", "game.playnow"); Tg("td_play", "game.playnow");
            var ws = root.Q<Label>("world_status");
            if (ws != null && (ws.text == "Hazır." || ws.text == "Ready." || ws.text == "就绪。" || ws.text == "就緒。"))
                ws.text = NovaLocale.T("world.ready");   // yalnız boştaysa çevir, çalışma mesajını ezme
            if (_gameType != null)
            {
                int keep = _gameType.index;
                _gameType.choices = GameTypeChoices();
                _gameType.index = Mathf.Clamp(keep, 0, GameTypeChoices().Count - 1);
            }
            ApplyGameType(); // dil değişince ipucu da güncellensin
            Tg("prep_navmesh", "prep.navmesh"); Tg("prep_spawn", "prep.spawn"); Tg("prep_minimap", "prep.minimap");

            // ---- AYARLAR sekmesi (tek alan, otomatik kurulum) ----
            L("lbl_set_title", "set.title");
            L("lbl_set_head_hint", "set.headHint");
            L("lbl_step1", "set.pasteKey");
            L("set_provider_hint", "set.autoNote");
            L("lbl_set_saved", "set.activeTitle");
            L("set_privacy", "set.privacy");
            L("hint_custom", "set.ownServer");
            L("hint_examples", "set.addrNote");
            Dd("set_preset", "set.preset.label");
            B("set_key_save", "set.btn.connect");
            B("set_test", "set.btn.test");
            B("set_reload", "set.btn.reload");
            var setKeyF = root.Q<TextField>("set_key");
            if (setKeyF != null) setKeyF.textEdition.placeholder = NovaLocale.T("set.key.placeholder");
            var setBaseF = root.Q<TextField>("custom_base");
            if (setBaseF != null) setBaseF.label = NovaLocale.T("set.customBase");
            var setAdv = root.Q<Foldout>("set_adv");
            if (setAdv != null) setAdv.text = NovaLocale.T("set.ownServerTitle");

            B("mat_generate", "mat.generate"); B("mat_revert", "mat.revert");
            B("studio_generate", "studio.generate"); B("studio_add", "studio.add"); B("studio_clear", "studio.clear");

            // Listeler (seçili öğe korunur)
            if (_worldType != null)
            {
                int keep = _worldType.index;
                _worldType.choices = WTypes.Select(t => t.L).ToList();
                _worldType.index = Mathf.Clamp(keep, 0, WTypes.Length - 1);
            }
            if (_worldSize != null)
            {
                int keep = _worldSize.index;
                _worldSize.choices = new List<string> { NovaLocale.T("size.small"), NovaLocale.T("size.medium"), NovaLocale.T("size.large") };
                _worldSize.index = Mathf.Clamp(keep, 0, 2);
            }
            if (_worldSky != null)
            {
                int keep = _worldSky.index;
                var sky = new List<string>(SkyboxPresets.LocalizedNames());
                foreach (var h in HdriSky.Available()) sky.Add("🌤 " + NovaLocale.Mood(h.Mood) + " · " + h.Title);
                _worldSky.choices = sky;
                _worldSky.index = Mathf.Clamp(keep, 0, sky.Count - 1);
            }
            ApplyWorldType(); // açıklama metni de dile uysun
        }

        /// <summary>Oyun tipi listesi — sıra ApplyGameType ile aynı olmalı.</summary>
        private static List<string> GameTypeChoices() => new List<string>
        {
            NovaLocale.T("game.type.openworld"),   // 0
            NovaLocale.T("game.type.runner"),      // 1
            NovaLocale.T("game.type.arena"),       // 2
            NovaLocale.T("game.type.platformer"),  // 3
            NovaLocale.T("game.type.racer"),       // 4
            NovaLocale.T("game.type.td"),          // 5
            NovaLocale.T("game.type.2d"),          // 6
        };

        // Oyun tipi seçimine göre alt menü gruplarını göster/gizle
        private void ApplyGameType()
        {
            int i = _gameType != null ? _gameType.index : 0;
            void Show(VisualElement el, bool on) { if (el != null) el.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; }
            Show(_grpOpenWorld, i == 0);
            Show(_grpRunner, i == 1);
            Show(_grpArena, i == 2);
            Show(_grpPlatformer, i == 3);
            Show(_grpRacer, i == 4);
            Show(_grpTd, i == 5);
            Show(_grp2d, i == 6);
            var hint = rootVisualElement.Q<Label>("game_type_hint");
            if (hint != null)
                hint.text = i == 0 ? NovaLocale.T("game.type.openworld.h")
                          : i == 1 ? NovaLocale.T("game.type.runner.h")
                          : i == 2 ? NovaLocale.T("game.type.arena.h")
                          : i == 3 ? NovaLocale.T("game.type.platformer.h")
                          : i == 4 ? NovaLocale.T("game.type.racer.h")
                          : i == 5 ? NovaLocale.T("game.type.td.h")
                          : NovaLocale.T("game.type.2d.h");
        }

        // Tip değişince: bileşen varsayılanlarını ve etkin/pasif durumlarını ayarla
        private void ApplyWorldType()
        {
            int i = _worldType != null ? Mathf.Clamp(_worldType.index, 0, WTypes.Length - 1) : 0;
            var t = WTypes[i];
            bool city = t.Mode == "city";
            if (_worldTypeHint != null) _worldTypeHint.text = t.LH;

            void Set(Toggle tg, bool val, bool enabled)
            { if (tg == null) return; tg.SetValueWithoutNotify(val && enabled); tg.SetEnabled(enabled); }

            Set(_wTrees, t.Trees, true);
            Set(_wBushes, t.Bushes, true);
            Set(_wRocks, t.Rocks, !city);        // şehirde kaya saçmıyoruz
            Set(_wRiver, t.River, !city);        // ırmak/göl şimdilik arazi haritalarında
            Set(_wLake, t.Lake, !city);
            Set(_wVehicles, t.Vehicles, city);   // araç/sokak öğesi sadece şehirde
            Set(_wProps, t.Props, city);
        }

        // ---- [KULLANIM DIŞI — 2026-07] Doğal dilden arazi (backend /v1/world/terrain) ----
        // Tarifle kurma UI'dan kaldırıldı (elle seçim daha öngörülebilir bulundu).
        // Backend endpoint'i ve bu kod duruyor; ileride sohbet aracına bağlanabilir.
        private static readonly System.Net.Http.HttpClient WorldHttp =
            new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(60) };

        private async void BuildWorldFromPrompt(string prompt)
        {
            void Log(string msg) { if (_worldStatus != null) _worldStatus.text = msg; }
            Log("🧠 Beyin haritayı planlıyor...");
            try
            {
                var body = new Dictionary<string, object>
                {
                    { "prompt", prompt },
                    { "biomes", WTypes.Where(t => t.Mode == "terrain").Select(t => t.Biome).Distinct().ToList() },
                    { "skies", _worldSky != null ? new List<object>(_worldSky.choices) : new List<object>() },
                };
                using var req = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Post, _baseUrl + "/v1/world/terrain");
                req.Content = new System.Net.Http.StringContent(
                    Json.Serialize(body), Encoding.UTF8, "application/json");
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + UnityAIConfig.ApiToken);
                using var resp = await WorldHttp.SendAsync(req);
                string txt = await resp.Content.ReadAsStringAsync();

                // SESSİZ FALLBACK YOK: plan alınamazsa kurulum DURUR, neden açıkça yazılır.
                // (Aksi halde kullanıcının tarifiyle alakasız bir harita kurulur — güven kaybı.)
                if ((int)resp.StatusCode == 404)
                {
                    Log("⚠ Backend eski: /v1/world/terrain yok. 'cd backend && npm run dev' ile yeniden başlat.");
                    return;
                }
                if (!(Json.Deserialize(txt) is Dictionary<string, object> r) ||
                    !(r.TryGetValue("plan", out var pv) && pv is Dictionary<string, object> plan))
                {
                    Log("⚠ Plan alınamadı (yanıt çözümlenemedi) — Console'a bak, kurulum yapılmadı.");
                    Debug.LogWarning("[Nova] /world/terrain yanıtı: " + (txt?.Length > 300 ? txt.Substring(0, 300) + "…" : txt));
                    return;
                }
                string src = r.TryGetValue("source", out var sv) ? sv?.ToString() : "?";

                string Fs(string k, string d) => plan.TryGetValue(k, out var v) ? v?.ToString() ?? d : d;
                bool FbV(string k, bool d) => plan.TryGetValue(k, out var v) && v is bool b ? b : d;
                float Ff(string k, float d)
                {
                    if (!plan.TryGetValue(k, out var v)) return d;
                    if (v is double dd) return (float)dd;
                    if (v is long l) return l;
                    return d;
                }

                var tp = new TerrainPlan
                {
                    biome = Fs("biome", "plains"),
                    size = Mathf.RoundToInt(Ff("size", 400f)),
                    river = FbV("river", false),
                    lake = FbV("lake", false),
                    riverCurve = Ff("riverCurve", 0.5f),
                    trees = FbV("trees", true),
                    rocks = FbV("rocks", true),
                    bushes = FbV("bushes", true),
                    density = Ff("density", 0.6f),
                };
                // Seçimleri planla senkronla — kullanıcı beynin ne seçtiğini görür ve düzeltebilir
                SyncWorldControls(tp, Fs("sky", ""));

                int seed = new System.Random().Next();
                string summary = Fs("summary", prompt);
                Debug.Log($"[Nova] AI ARAZİ PLANI ({src}) · '{prompt}' → biome={tp.biome} · {tp.size}m · " +
                          $"yoğunluk {tp.density:0.00} · nehir={tp.river}(kıvrım {tp.riverCurve:0.0}) · göl={tp.lake} · seed {seed}");
                Log(src == "ai" ? $"🧠 Plan hazır: {summary} — kuruluyor..."
                                : $"⚠ AI'ya ulaşılamadı ({src}) — kaba kural planıyla kuruluyor: {summary}");
                TerrainGen.Build(tp, seed, Log);
            }
            catch (Exception e)
            {
                Log("⚠ Beyne ulaşılamadı: " + e.Message + " — backend çalışıyor mu? Kurulum yapılmadı.");
            }
        }

        /// <summary>Beynin seçtiği planı soldaki kontrollere yansıtır (şeffaflık + elle düzeltme).</summary>
        private void SyncWorldControls(TerrainPlan tp, string sky)
        {
            int ti = Array.FindIndex(WTypes, w => w.Mode == "terrain" && w.Biome == tp.biome);
            if (_worldType != null && ti >= 0) _worldType.index = ti; // callback ApplyWorldType'ı tetikler
            if (_wTrees != null) _wTrees.SetValueWithoutNotify(tp.trees);
            if (_wBushes != null) _wBushes.SetValueWithoutNotify(tp.bushes);
            if (_wRocks != null) _wRocks.SetValueWithoutNotify(tp.rocks);
            if (_wRiver != null) _wRiver.SetValueWithoutNotify(tp.river);
            if (_wLake != null) _wLake.SetValueWithoutNotify(tp.lake);
            if (_worldDensity != null) _worldDensity.SetValueWithoutNotify(tp.density);
            if (_worldSize != null) _worldSize.index = tp.size <= 300 ? 0 : tp.size <= 500 ? 1 : 2;
            if (!string.IsNullOrEmpty(sky) && _worldSky != null)
            {
                int si = _worldSky.choices.IndexOf(sky);
                if (si >= 0 && si != _worldSky.index) _worldSky.index = si; // callback ApplySky'ı tetikler
            }
        }

        // Seçimlerden deterministik reçete kur, ilgili motoru çağır.
        private void BuildWorldFromControls()
        {
            int i = _worldType != null ? Mathf.Clamp(_worldType.index, 0, WTypes.Length - 1) : 0;
            var t = WTypes[i];
            int sizeIdx = _worldSize != null ? _worldSize.index : 1;
            float density = _worldDensity != null ? _worldDensity.value : 0.6f;
            // Seed: kutu doluysa aynı haritayı yeniden kurar; boşsa rastgele üret ve kutuya yaz
            int seed;
            if (_worldSeed == null || !int.TryParse(_worldSeed.value?.Trim(), out seed))
                seed = new System.Random().Next();
            if (_worldSeed != null) _worldSeed.SetValueWithoutNotify(seed.ToString());
            void Log(string msg) { if (_worldStatus != null) _worldStatus.text = msg; }

            if (t.Mode == "city")
            {
                // FAZ 2: organik şehir — ızgara yok; kıvrımlı yollar + parseller + zonlama
                float sizeM = sizeIdx == 0 ? 300f : sizeIdx == 1 ? 450f : 650f;
                var plan = new WorldPlan
                {
                    style = "any",
                    theme = t.Name == "Modern Şehir" ? "modern"
                          : t.Name == "Kasaba / Köy" || t.Name == "Ortaçağ Köyü" ? "rural" : "any",
                    density = t.Name == "Modern Şehir" ? density : density * 0.75f,
                    greenery = _wTrees != null && _wTrees.value ? (t.Name == "Modern Şehir" ? 0.3f : 0.5f) : 0f,
                    vehicles = _wVehicles != null && _wVehicles.value,
                    props = _wProps != null && _wProps.value,
                    summary = t.Name,
                };
                Debug.Log($"[Nova] REÇETE · {t.Name} (organik) · seed {seed} · {sizeM:0}m · yoğunluk {plan.density:0.00}\n" +
                          $"  roller: house,shop,civic,tower (assets-raw/houses|apartments|shops|civic)" +
                          (plan.greenery > 0 ? " + tree,bush (trees|bushes)" : "") +
                          (plan.vehicles ? " + car,truck (vehicles)" : "") +
                          (plan.props ? " + lamp,bench (streetlights|benches)" : "") +
                          "\n  Palet asset'leri aşağıda '[Nova] Palet:' satırlarında — tutarsız görüneni oradan tespit et.");
                Log(NovaLocale.T("world.status.typeBuilding", t.L));
                WorldBuilderAI.BuildOrganic(plan, sizeM, seed, Log);
            }
            else
            {
                int m = sizeIdx == 0 ? 220 : sizeIdx == 1 ? 400 : 600;
                var tp = new TerrainPlan
                {
                    biome = t.Biome,
                    size = m,
                    river = _wRiver != null && _wRiver.value,
                    lake = _wLake != null && _wLake.value,
                    riverCurve = _worldRiverCurve != null ? _worldRiverCurve.value : 0.5f,
                    relief = _worldRelief != null ? _worldRelief.value : 0.5f,
                    trees = _wTrees != null && _wTrees.value,
                    rocks = _wRocks != null && _wRocks.value,
                    bushes = _wBushes != null && _wBushes.value,
                    flowers = _wFlowers != null && _wFlowers.value,
                    path = _wPath != null && _wPath.value,
                    density = density,
                    treeMul = _treeMul != null ? _treeMul.value : 1f,
                    rockMul = _rockMul != null ? _rockMul.value : 1f,
                    bushMul = _bushMul != null ? _bushMul.value : 1f,
                    addPlayer = _wPlayer == null || _wPlayer.value, // Play'e hazır oyuncu
                };
                ApplyAtmosphere(); // sis/güneş/rüzgâr ayarları yeni haritada da geçerli olsun
                Debug.Log($"[Nova] REÇETE · {t.Name} · seed {seed} · {m}m · yoğunluk {density:0.00}\n" +
                          $"  biome: {tp.biome}{(tp.river ? " + ırmak" : "")}{(tp.lake ? " + göl" : "")}" +
                          $"\n  roller: {(tp.trees ? "tree (assets-raw/trees) " : "")}{(tp.rocks ? "rock (rocks) " : "")}{(tp.bushes ? "bush (bushes)" : "")}" +
                          "\n  dokular: textures-raw (grass/rock/sand katmanları)");
                Log(NovaLocale.T("world.status.typeBuilding", t.L));
                TerrainGen.Build(tp, seed, Log);
            }
        }

        /// <summary>
        /// Atmosfer: sis + güneş açısı + rüzgâr. Harita kurmadan da anında uygulanır.
        /// Güneş "Gökyüzüne göre" ise dokunulmaz (HdriSky ışığı kendi hizalar).
        /// </summary>
        private void ApplyAtmosphere()
        {
            // ---- Sis ----
            bool fog = _wFog != null && _wFog.value;
            RenderSettings.fog = fog;
            if (fog)
            {
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogDensity = _fogDens != null ? _fogDens.value : 0.005f;
                RenderSettings.fogColor = new Color(0.75f, 0.78f, 0.82f);
            }

            // ---- Güneş ----
            int sunIdx = _worldSun != null ? _worldSun.index : 0;
            if (sunIdx > 0)
            {
                var sun = RenderSettings.sun;
                if (sun == null)
                    sun = UnityEngine.Object.FindObjectsByType<Light>()
                        .FirstOrDefault(l => l.type == LightType.Directional);
                if (sun != null)
                {
                    float elev = sunIdx == 1 ? 15f : sunIdx == 2 ? 62f : 8f;   // sabah / öğle / akşam
                    float yaw = sunIdx == 3 ? 250f : 45f;
                    Undo.RecordObject(sun.transform, "Nova: Güneş");
                    Undo.RecordObject(sun, "Nova: Güneş");
                    sun.transform.rotation = Quaternion.Euler(elev, yaw, 0f);
                    sun.intensity = sunIdx == 2 ? 1.15f : 0.85f;
                    sun.color = sunIdx == 3 ? new Color(1f, 0.72f, 0.5f)
                              : sunIdx == 1 ? new Color(1f, 0.92f, 0.82f) : Color.white;
                }
            }

            // ---- Rüzgâr (WindZone) ----
            bool wind = _wWind != null && _wWind.value;
            var wz = GameObject.Find("NovaWind");
            if (wind && wz == null)
            {
                wz = new GameObject("NovaWind");
                var z = wz.AddComponent<WindZone>();
                z.mode = WindZoneMode.Directional;
                z.windMain = 0.6f;
                z.windTurbulence = 0.4f;
                z.windPulseMagnitude = 0.4f;
                z.windPulseFrequency = 0.15f;
                Undo.RegisterCreatedObjectUndo(wz, "Nova: Rüzgâr");
            }
            else if (!wind && wz != null)
                Undo.DestroyObjectImmediate(wz);
        }

        private void OnWorldExplore()
        {
            WorldExplorer.SpawnAndPlay(msg => { if (_worldStatus != null) _worldStatus.text = msg; });
        }

        private void ApplySky()
        {
            int i = _worldSky != null ? _worldSky.index : 0;
            void Log(string msg) { if (_worldStatus != null) _worldStatus.text = msg; }
            int procedural = SkyboxPresets.Names.Length;
            if (i < procedural) SkyboxPresets.Apply(i, Log);
            else HdriSky.Apply(i - procedural, Log); // gerçek HDRI gökyüzü
        }

        private static void SetActive(Button b, bool active)
        {
            if (b == null) return;
            if (active) b.AddToClassList("tab-active");
            else b.RemoveFromClassList("tab-active");
        }

        // --- 3D Stüdyo: önce önizle, beğenirsen sahneye ekle ---
        private void OnStudioGenerate()
        {
            string prompt = _studioPrompt?.value?.Trim();
            bool imgMode = _studioMode != null && _studioMode.index == 1;
            string image = null;
            if (imgMode)
            {
                image = !string.IsNullOrEmpty(_imgDataUri) ? _imgDataUri
                      : !string.IsNullOrEmpty(_imgUrl) ? _imgUrl
                      : _studioImage?.value?.Trim();
                if (string.IsNullOrEmpty(image))
                {
                    if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.needImage");
                    return;
                }
            }
            else if (string.IsNullOrEmpty(prompt))
            {
                if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.enterPrompt");
                return;
            }

            if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.generating");
            string name = string.IsNullOrEmpty(prompt) ? "GeneratedModel" : prompt;
            _lastPrompt = prompt;
            _isRigged = false; _walkUrl = null;
            int faceLimit = _studioQuality == null ? 0
                : (_studioQuality.index == 2 ? 12000 : (_studioQuality.index == 1 ? 30000 : 0));
            StartGenProgress();
            ModelGenerator.GeneratePreview(
                _baseUrl, UnityAIConfig.ApiToken, imgMode ? null : prompt, image, name, faceLimit,
                OnPreviewReady, OnGenStep,
                msg => { if (_studioStatus != null) _studioStatus.text = msg; });
        }

        private void UpdateStudioMode()
        {
            bool imgMode = _studioMode != null && _studioMode.index == 1;
            if (_studioImageRow != null)
                _studioImageRow.style.display = imgMode ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnImageUpload()
        {
            string path = EditorUtility.OpenFilePanel("Görsel seç", "", "png,jpg,jpeg,webp");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                string mime = (ext == ".jpg" || ext == ".jpeg") ? "image/jpeg" : ext == ".webp" ? "image/webp" : "image/png";
                _imgDataUri = "data:" + mime + ";base64," + System.Convert.ToBase64String(bytes);
                _imgUrl = null;
                ShowThumb(bytes);
                if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.imageUploaded");
            }
            catch (System.Exception e)
            {
                if (_studioStatus != null) _studioStatus.text = NovaLocale.T("attach.imageReadError", e.Message);
            }
        }

        private void OnImageGenerate()
        {
            string prompt = _studioPrompt?.value?.Trim();
            if (string.IsNullOrEmpty(prompt))
            {
                if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.promptForImage");
                return;
            }
            if (!EditorUtility.DisplayDialog(NovaLocale.T("dialog.genImage.title"),
                NovaLocale.T("dialog.genImage2.body"),
                NovaLocale.T("dialog.generate"), NovaLocale.T("dialog.cancel"))) return;
            if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.generatingImage");
            ModelGenerator.GenerateImage(_baseUrl, UnityAIConfig.ApiToken, prompt, OnImageGenerated,
                msg => { if (_studioStatus != null) _studioStatus.text = msg; });
        }

        private void OnImageGenerated(string url)
        {
            _imgUrl = url;
            _imgDataUri = null;
            if (_studioImage != null) _studioImage.value = url;
            if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.imageReady");
            DownloadThumb(url);
        }

        private void ShowThumb(byte[] bytes)
        {
            if (_studioImgPreview == null) return;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(bytes))
            {
                _studioImgPreview.image = tex;
                _studioImgPreview.style.display = DisplayStyle.Flex;
            }
        }

        private async void DownloadThumb(string url)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                var bytes = await http.GetByteArrayAsync(url);
                EditorApplication.delayCall += () => ShowThumb(bytes);
            }
            catch { }
        }

        private void OnPreviewReady(GameObject go, string name, string glbUrl, string generator)
        {
            StopGenProgress();
            if (_preview == null) return;
            _preview.SetModel(go, name, glbUrl);
            _isRigged = false; _walkUrl = null;
            float sz = (_studioSize != null && _studioSize.index >= 0 && _studioSize.index < SizeVals.Length) ? SizeVals[_studioSize.index] : 0f;
            if (sz > 0f) _preview.ScaleToHeight(sz);
            double secs = EditorApplication.timeSinceStartup - _genStart;
            _lastSecs = secs;
            SetStats(_preview.ComputeStats(), generator, secs);
            if (_studioAdd != null) _studioAdd.SetEnabled(true);
            if (_studioRig != null) _studioRig.SetEnabled(true);
            if (_studioPreview != null) _studioPreview.MarkDirtyRepaint();

            // Sohbetten başlatıldıysa turu sürdür (ajan kapanış mesajını yazsın)
            FinishChatModelGeneration(true, null);
        }

        private void OnStudioAdd()
        {
            if (_preview == null || !_preview.HasModel) return;
            if (_isRigged && !string.IsNullOrEmpty(_walkUrl))
            {
                ModelGenerator.PlaceAnimatedFromUrl(_walkUrl, "Character", Vector3.zero, _riggedH,
                    msg => { if (_studioStatus != null) _studioStatus.text = msg; });
                AddRow(_studioGallery, NovaLocale.T("studio.charAdded"));
                if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.charAddedStatus");
                return;
            }
            // Kalıcılık: GLB'yi projeye kaydet + kalıcı prefab olarak sahnele (runtime değil)
            string gurl = _preview.GlbUrl;
            if (!string.IsNullOrEmpty(gurl))
            {
                if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.savingAndPlacing");
                AssetSaver.SaveGlbAndPlace(gurl, _preview.ModelName, _preview.CurrentHeight(),
                    msg => { if (_studioStatus != null) _studioStatus.text = msg; });
                AddRow(_studioGallery, NovaLocale.T("studio.persistentAdded"));
            }
            else
            {
                var go = _preview.InstantiateIntoScene(Vector3.zero);
                if (go != null)
                {
                    AddRow(_studioGallery, NovaLocale.T("studio.addedTemp", go.name));
                    if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.addedTempStatus", go.name);
                }
            }
        }

        private void OnStudioClear()
        {
            _preview?.ClearModel();
            if (_studioAdd != null) _studioAdd.SetEnabled(false);
            if (_studioRig != null) _studioRig.SetEnabled(false);
            SetEditEnabled(false);
            _isRigged = false; _walkUrl = null;
            if (_studioPreview != null) _studioPreview.MarkDirtyRepaint();
            if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.cleared");
            ShowStatsPlaceholder(NovaLocale.T("studio.statsPlaceholder"));
        }

        // ---- Rigleme ----
        private void OnStudioRig()
        {
            if (_preview == null || !_preview.HasModel) { if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.needModelFirst"); return; }
            string url = _preview.GlbUrl;
            if (string.IsNullOrEmpty(url)) { if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.noModelUrl"); return; }
            if (!EditorUtility.DisplayDialog(NovaLocale.T("dialog.rig.title"),
                NovaLocale.T("dialog.rig2.body"),
                NovaLocale.T("dialog.rigConfirm"), NovaLocale.T("dialog.cancel"))) return;
            if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.rigging");
            int[] animIds = (_studioAnim != null && _studioAnim.index > 0)
                ? new int[] { AnimIds[_studioAnim.index - 1] } : null;
            _riggedH = _preview.CurrentHeight(); // rig sonrası bu boya geri ölçekle -> jump yok
            float height = _riggedH > 0f ? _riggedH : 1.8f;
            StartGenProgress(RigSteps);
            ModelGenerator.RigAndAnimate(_baseUrl, UnityAIConfig.ApiToken, url, animIds, height, OnRigReady, OnGenStep,
                msg => { if (_studioStatus != null) _studioStatus.text = msg; });
        }

        private void OnRigReady(GameObject go, string riggedUrl, string walkUrl)
        {
            StopGenProgress();
            if (_preview == null) return;
            _preview.SetModel(go, "RiggedCharacter", riggedUrl);
            if (_riggedH > 0f) _preview.ScaleToHeight(_riggedH); // üretilen boyla aynı kalsın
            _isRigged = true;
            _walkUrl = walkUrl;
            SetStats(_preview.ComputeStats(), "Nova 3D", _lastSecs);
            if (_studioAdd != null) _studioAdd.SetEnabled(true);
            if (_studioPreview != null) _studioPreview.MarkDirtyRepaint();
            if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.rigged");
        }

        // ---- Üretim progress ----
        private void StartGenProgress(string[] steps = null)
        {
            _activeSteps = steps ?? GenSteps;
            _genActive = true;
            _genStep = 0;
            _genStart = EditorApplication.timeSinceStartup;
            if (!_tickOn) { EditorApplication.update += StudioTick; _tickOn = true; }
            ShowStatsPlaceholder("Üretiliyor...");
        }

        private void StopGenProgress()
        {
            _genActive = false;
            if (_tickOn) { EditorApplication.update -= StudioTick; _tickOn = false; }
        }

        private void OnGenStep(int index, string label)
        {
            if (index < 0)
            {
                StopGenProgress();
                if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.errorPrefix", label);
                FinishChatModelGeneration(false, label);
            }
            else
            {
                _genStep = index;
                if (_studioStatus != null) _studioStatus.text = NovaLocale.T("studio.stepEllipsis", label);
                // Sohbette bekleyen kullanıcı da hangi aşamada olduğumuzu görsün
                if (_pendingModelCallId != null)
                {
                    if (_modelProgressLabel != null)
                        _modelProgressLabel.text = NovaLocale.T("gen3d.stepInProgress", _pendingModelName, label);
                    SetStatus(NovaLocale.T("gen3d.stepStatus", label));
                }
            }
            if (_studioPreview != null) _studioPreview.MarkDirtyRepaint();
        }

        private void StudioTick()
        {
            if (_genActive && _studioPreview != null) _studioPreview.MarkDirtyRepaint();
        }

        private void DrawGenOverlay(Rect r)
        {
            if (_ovTitle == null)
            {
                _ovTitle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                _ovStep = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.9f, 0.9f, 0.9f) } };
            }
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.6f));

            float elapsed = (float)(EditorApplication.timeSinceStartup - _genStart);
            float timeP = 1f - Mathf.Exp(-elapsed / 16f);
            var steps = _activeSteps ?? GenSteps;
            int lastIdx = Mathf.Max(1, steps.Length - 1);
            float floor = Mathf.Clamp01((float)_genStep / lastIdx) * 0.92f;
            float pct = Mathf.Clamp01(Mathf.Max(floor, Mathf.Lerp(0.08f, 0.9f, timeP)));

            float pw = Mathf.Min(320f, r.width * 0.7f);
            float cx = r.center.x;
            float top = r.center.y - 70f;

            GUI.Label(new Rect(cx - pw / 2f, top, pw, 20f), NovaLocale.T("studio.generatingOverlay", Mathf.RoundToInt(pct * 100f)), _ovTitle);

            var barBg = new Rect(cx - pw / 2f, top + 24f, pw, 10f);
            EditorGUI.DrawRect(barBg, new Color(1f, 1f, 1f, 0.15f));
            EditorGUI.DrawRect(new Rect(barBg.x, barBg.y, barBg.width * pct, barBg.height), new Color(0.133f, 0.827f, 0.933f, 1f)); // cyan aksan (#22d3ee)

            float sy = barBg.y + 20f;
            for (int i = 0; i < steps.Length; i++)
            {
                string mark = i < _genStep ? "✓" : (i == _genStep ? "▶" : "○");
                GUI.Label(new Rect(cx - pw / 2f, sy + i * 16f, pw, 16f), mark + "  " + steps[i], _ovStep);
            }
        }

        // ---- Sağ panel: model bilgileri ----
        private void ShowStatsPlaceholder(string text)
        {
            if (_studioStats == null) return;
            _studioStats.Clear();
            var t = new Label(NovaLocale.T("studio.modelInfo")); t.AddToClassList("panel-title"); _studioStats.Add(t);
            var p = new Label(text); p.AddToClassList("status"); p.style.whiteSpace = WhiteSpace.Normal; _studioStats.Add(p);
        }

        private void SetStats(ModelPreview.Stats st, string generator, double seconds)
        {
            if (_studioStats == null) return;
            _studioStats.Clear();
            var t = new Label(NovaLocale.T("studio.modelInfo")); t.AddToClassList("panel-title"); _studioStats.Add(t);
            AddStat("Vertices", st.Vertices.ToString("N0"));
            if (st.Triangles > 100000) AddStatWarn("Triangles", st.Triangles.ToString("N0"));
            else AddStat("Triangles", st.Triangles.ToString("N0"));
            AddStat("Materials", st.Materials.ToString());
            AddStat("Textures", st.Textures.ToString());
            AddStat(NovaLocale.T("studio.size"), $"{st.Size.x:0.##}x{st.Size.y:0.##}x{st.Size.z:0.##}");
            AddStat("LOD", NovaLocale.T("studio.lodNone"));
            AddStat("Generator", "Nova 3D");
            AddStat(NovaLocale.T("studio.duration"), NovaLocale.T("studio.durationVal", seconds));
            if (_isRigged)
            {
                AddStat(NovaLocale.T("studio.rig"), NovaLocale.T("studio.rigHumanoid"));
                AddStat(NovaLocale.T("studio.animation"), "walk, run");
            }
        }

        private void AddStat(string key, string val)
        {
            if (_studioStats == null) return;
            var row = new VisualElement(); row.AddToClassList("stat-row");
            var k = new Label(key); k.AddToClassList("stat-key");
            var v = new Label(val); v.AddToClassList("stat-val");
            row.Add(k); row.Add(v);
            _studioStats.Add(row);
        }

        private void AddStatWarn(string key, string val)
        {
            if (_studioStats == null) return;
            var row = new VisualElement(); row.AddToClassList("stat-row");
            var k = new Label(key); k.AddToClassList("stat-key");
            var v = new Label(val + " ⚠"); v.AddToClassList("stat-val");
            v.style.color = new Color(0.96f, 0.62f, 0.22f);
            row.Add(k); row.Add(v);
            _studioStats.Add(row);
            var hint = new Label(NovaLocale.T("studio.highForMobileHint"));
            hint.AddToClassList("status"); hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.color = new Color(0.96f, 0.62f, 0.22f);
            _studioStats.Add(hint);
        }

        private void OnModelGenerated(string name, string url)
        {
            AddRow(_studioGallery, $"✓ {name}");
        }

        private void OnEditProposed(string path)
        {
            SwitchTab("chat"); // yeni öneriyi kullanıcının önüne getir (kod bölümü sohbette)
        }

        private void RebuildCodeList()
        {
            if (_codeList == null) return;
            _codeList.Clear();
            int pendingCount = 0;
            foreach (var e in CodeEdits.Pending)
            {
                pendingCount++;
                var card = new VisualElement();
                card.AddToClassList("edit-card");
                var pathLabel = new Label(e.Path);
                pathLabel.AddToClassList("edit-path");
                card.Add(pathLabel);

                var diff = DiffUtil.LineDiff(e.OldText, e.NewText);
                int shown = 0;
                foreach (var dl in diff)
                {
                    if (shown > 500) break;
                    var line = new Label((dl.Tag == ' ' ? "  " : dl.Tag + " ") + dl.Text);
                    line.AddToClassList("diff-line");
                    line.AddToClassList(dl.Tag == '+' ? "diff-add" : dl.Tag == '-' ? "diff-del" : "diff-ctx");
                    card.Add(line);
                    shown++;
                }

                var actions = new VisualElement();
                actions.AddToClassList("edit-actions");
                string id = e.Id;
                var apply = new Button(() => CodeEdits.Apply(id)) { text = NovaLocale.T("code.apply") };
                apply.AddToClassList("btn-apply");
                var reject = new Button(() => CodeEdits.Reject(id)) { text = NovaLocale.T("code.reject") };
                reject.AddToClassList("btn-reject");
                actions.Add(apply);
                actions.Add(reject);
                card.Add(actions);
                _codeList.Add(card);
            }
            // Bekleyen diff yoksa bölümü tamamen gizle (sohbet ferah kalsın)
            if (_codeSection != null)
                _codeSection.style.display = pendingCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void AddRow(ScrollView list, string text)
        {
            if (list == null) return;
            var row = new Label(text);
            row.AddToClassList("msg-body");
            row.style.paddingTop = 3; row.style.paddingBottom = 3;
            list.Add(row);
            list.scrollOffset = new Vector2(0, float.MaxValue);
        }

        // Hızlı aksiyon: hazır bir prompt'u sohbete yaz ve çalıştır.
        private void RunPrompt(string text)
        {
            if (_running) return;
            SwitchTab("chat");
            if (_input != null) _input.value = text;
            OnSubmit();
        }

        // --- Agent döngüsü (Sohbet) ---
        // ═══════════ GÖRSEL EKLEME ═══════════
        // Kullanıcı ekran görüntüsü/foto ekler; backend bunu vision modeline okutur,
        // metne çevirip ana beyne verir. Böylece "şu ekrandaki hatayı düzelt" çalışır.

        private const int MaxImageDim = 768;   // vision jeton sınırı için üst boyut
        private const int MaxDocChars = 12000; // belgeden alınacak en fazla karakter

        /// <summary>"+" menüsü: görsel, belge, ekran görüntüsü, panodan yapıştır.</summary>
        private void ShowPlusMenu(Button anchor)
        {
            var m = new GenericMenu();
            m.AddItem(new GUIContent(NovaLocale.T("menu.addImage")), false, OnAttachImageFromDisk);
            m.AddItem(new GUIContent(NovaLocale.T("menu.addDoc")), false, OnAttachDocument);
            m.AddSeparator("");
            m.AddItem(new GUIContent(NovaLocale.T("menu.sceneShot")), false, OnAttachSceneShot);
            m.AddItem(new GUIContent(NovaLocale.T("menu.pasteImage")), false, () =>
            {
                if (!TryPasteImageFromClipboard())
                    SetStatus(NovaLocale.T("attach.noImageOnClipboard"));
            });
            m.DropDown(anchor.worldBound);
        }

        /// <summary>"≡" menüsü: sahne araçları ve hazır görevler.</summary>
        private void ShowToolsMenu(Button anchor)
        {
            var m = new GenericMenu();
            m.AddItem(new GUIContent(NovaLocale.T("menu.scanScene")), false,
                () => AppendMessage(NovaLocale.T("chat.role.auditor"), SceneHealth.ScanAndReport()));
            m.AddItem(new GUIContent(NovaLocale.T("menu.fixConsole")), false, () => RunPrompt(
                "Konsoldaki derleme/hatalarını oku ve düzelt. Hatadaki dosyayı ReadScript ile oku, " +
                "WriteScript ile minimal bir diff öner."));
            m.AddSeparator("");
            m.AddItem(new GUIContent(NovaLocale.T("menu.presetPlayer")), false, () => RunPrompt(
                "Bana bir 3D karakter kontrolcüsü (WASD hareket + zıplama, CharacterController tabanlı) C# " +
                "script'i yaz. WriteScript ile Assets/Scripts/PlayerController.cs olarak öner."));
            m.AddItem(new GUIContent(NovaLocale.T("menu.presetHealth")), false, () => RunPrompt(
                "Bir can sistemi script'i yaz: maxHealth, currentHealth, TakeDamage, Heal ve ölüm eventi. " +
                "WriteScript ile Assets/Scripts/Health.cs olarak öner."));
            m.AddItem(new GUIContent(NovaLocale.T("menu.presetInventory")), false, () => RunPrompt(
                "Basit bir envanter sistemi script'i yaz: item ekle/çıkar/listele. " +
                "WriteScript ile Assets/Scripts/Inventory.cs olarak öner."));
            m.AddSeparator("");
            m.AddItem(new GUIContent(NovaLocale.T("menu.clearChat")), false, NewChat);
            m.DropDown(anchor.worldBound);
        }

        /// <summary>Panodaki görseli eke çevirir. Görsel yoksa false (metin yapıştırması bozulmaz).</summary>
        private bool TryPasteImageFromClipboard()
        {
            if (!NovaClipboard.TryGetImage(out var tex) || tex == null) return false;
            AddAttachment(tex, "pano-goruntusu.png");
            return true;
        }

        /// <summary>Belge (kod/metin) ekler — içeriği mesaja metin olarak iliştirilir.</summary>
        private void OnAttachDocument()
        {
            string path = EditorUtility.OpenFilePanel("Nova'ya belge ekle", "", "cs,txt,md,json,log,yaml,xml,shader");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string text = File.ReadAllText(path);
                bool truncated = text.Length > MaxDocChars;
                if (truncated) text = text.Substring(0, MaxDocChars);
                string name = Path.GetFileName(path);
                var a = new Attachment
                {
                    IsDoc = true,
                    Name = name,
                    Text = text + (truncated ? "\n… (belge kısaltıldı)" : ""),
                };
                var badge = new Label("📄 " + name) { tooltip = path };
                badge.AddToClassList("attach-doc");
                AddChip(a, badge);

                SetStatus(NovaLocale.T("attach.docAdded", name, text.Length));
            }
            catch (Exception e) { SetStatus(NovaLocale.T("attach.docReadError", e.Message)); }
        }

        private void OnAttachImageFromDisk()
        {
            string path = EditorUtility.OpenFilePanel("Nova'ya görsel ekle", "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(File.ReadAllBytes(path)))
                {
                    SetStatus(NovaLocale.T("attach.imageReadError", Path.GetFileName(path)));
                    return;
                }
                AddAttachment(tex, Path.GetFileName(path));
            }
            catch (Exception e) { SetStatus(NovaLocale.T("attach.imageAddError", e.Message)); }
        }

        private void OnAttachSceneShot()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null)
            {
                SetStatus(NovaLocale.T("attach.sceneViewClosed"));
                return;
            }
            var cam = sv.camera;
            int w = MaxImageDim;
            int h = Mathf.Max(1, Mathf.RoundToInt(w * (float)cam.pixelHeight / Mathf.Max(1, cam.pixelWidth)));
            var rt = new RenderTexture(w, h, 24);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                AddAttachment(tex, "sahne-goruntusu.png");
            }
            catch (Exception e) { SetStatus(NovaLocale.T("attach.screenshotError", e.Message)); }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private void AddAttachment(Texture2D tex, string label)
        {
            var small = Downscale(tex, MaxImageDim);
            byte[] png = small.EncodeToPNG();
            if (png == null || png.Length == 0) { SetStatus(NovaLocale.T("attach.imageEncodeError")); return; }

            var a = new Attachment { Base64 = Convert.ToBase64String(png), Tex = small, Name = label };
            var img = new Image { image = small, tooltip = label + NovaLocale.T("attach.zoomHint") };
            img.AddToClassList("attach-thumb");
            img.scaleMode = ScaleMode.ScaleToFit;
            img.RegisterCallback<ClickEvent>(_ => NovaImagePreview.Show(small, label));
            AddChip(a, img);

            SetStatus(NovaLocale.T("status.attachmentsReady", _attachments.Count));
        }

        /// <summary>Eki şeride yerleştirir; köşesine kendi ✕ düğmesini koyar.</summary>
        private void AddChip(Attachment a, VisualElement content)
        {
            _attachments.Add(a);
            if (_attachThumbs == null) return;

            var chip = new VisualElement();
            chip.AddToClassList("attach-chip");
            chip.Add(content);

            var x = new Button(() => RemoveAttachment(a)) { text = "✕", tooltip = NovaLocale.T("tooltip.removeAttachment") };
            x.AddToClassList("attach-x");
            chip.Add(x);

            a.Chip = chip;
            _attachThumbs.Add(chip);
            if (_attachStrip != null) _attachStrip.style.display = DisplayStyle.Flex;
        }

        private void RemoveAttachment(Attachment a)
        {
            if (a == null) return;
            a.Chip?.RemoveFromHierarchy();
            _attachments.Remove(a);
            if (_attachments.Count == 0 && _attachStrip != null)
                _attachStrip.style.display = DisplayStyle.None;
            SetStatus(_attachments.Count == 0 ? "Ekler kaldırıldı." : $"{_attachments.Count} ek kaldı.");
        }

        private void ClearAttachments()
        {
            _attachments.Clear();
            _attachThumbs?.Clear();
            if (_attachStrip != null) _attachStrip.style.display = DisplayStyle.None;
        }

        private static Texture2D Downscale(Texture2D src, int maxDim)
        {
            int w = src.width, h = src.height;
            if (Mathf.Max(w, h) <= maxDim) return src;
            float k = (float)maxDim / Mathf.Max(w, h);
            int nw = Mathf.Max(1, Mathf.RoundToInt(w * k)), nh = Mathf.Max(1, Mathf.RoundToInt(h * k));
            var rt = RenderTexture.GetTemporary(nw, nh, 0);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var dst = new Texture2D(nw, nh, TextureFormat.RGB24, false);
            dst.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }

        private void OnSubmit()
        {
            // AI çalışırken mesaj SESSİZCE YUTULMAZ: yazdığın metin kutuda kalır,
            // durum çubuğu neden gönderilmediğini söyler.
            if (_running)
            {
                SetStatus(NovaLocale.T("status.busyWait"));
                return;
            }

            string text = _input?.value?.Trim();
            // Sadece ek (görsel/belge) varsa da gönderilebilsin
            if (string.IsNullOrEmpty(text) && _attachments.Count > 0)
                text = _attachments.Exists(a => a.IsDoc) ? "Ekteki belgeye bak." : "Bu görsele bak.";
            if (string.IsNullOrEmpty(text)) return;
            if (_input != null) _input.value = "";

            var sys = new BackendClient.Message { Role = "system", Content = ContextCollector.BuildSystemPrompt() };
            if (_history.Count > 0 && _history[0].Role == "system") _history[0] = sys;
            else _history.Insert(0, sys);

            var imgList = _attachments.FindAll(a => !a.IsDoc).ConvertAll(a => a.Base64);
            var docList = _attachments.FindAll(a => a.IsDoc);
            var images = imgList.Count > 0 ? imgList : null;

            // Belgeler mesajın metnine iliştirilir (model dosyayı görsün)
            string payload = text;
            foreach (var d in docList)
                payload += $"\n\n[Ekli belge: {d.Name}]\n```\n{d.Text}\n```";

            var notes = new List<string>();
            if (images != null) notes.Add(NovaLocale.T("attach.imagesCount", images.Count));
            if (docList.Count > 0) notes.Add(NovaLocale.T("attach.docsCount", docList.Count));
            AppendMessage(NovaLocale.T("chat.role.you"),
                notes.Count > 0 ? NovaLocale.T("chat.msg.attachedSuffix", text, string.Join(" · ", notes)) : text);
            ClearAttachments();

            if (_pendingAskId != null)
            {
                // Nova soru sormuştu: bu mesaj o sorunun CEVABI (tool sonucu) olarak gider.
                _history.Add(new BackendClient.Message
                {
                    Role = "tool",
                    ToolCallId = _pendingAskId,
                    Content = Json.Serialize(new Dictionary<string, object> { { "ok", true }, { "answer", payload } }),
                });
                _pendingAskId = null;
            }
            else
            {
                _history.Add(new BackendClient.Message { Role = "user", Content = payload, Images = images });
                _turnGuard = 0;
            }
            StartTurn();
        }

        /// <summary>Çalışma durumunu tek yerden yönetir: girdi kilidi, Durdur butonu, durum yazısı.</summary>
        private void SetRunning(bool running, string status)
        {
            _running = running;
            if (_input != null) _input.SetEnabled(!running);
            if (_submitBtn != null) _submitBtn.style.display = running ? DisplayStyle.None : DisplayStyle.Flex;
            if (_stopBtn != null) _stopBtn.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            SetStatus(status);
            // Derleme tam bu sırada olursa, geri geldiğimizde turun kesildiğini bilelim.
            NovaChatState.WasInterrupted = running;
            SaveState();
        }

        private void StopRun()
        {
            if (!_running) return;
            try { _cts?.Cancel(); } catch { }
            _events.Clear();
            if (_pendingModelCallId != null)
            {
                // Üretim sunucuda sürüyor olabilir; turu kapatıyoruz, model gelirse Stüdyo'da belirir.
                _pendingModelCallId = null;
                StopGenProgress();
                AppendMessage(NovaLocale.T("chat.role.system"), NovaLocale.T("chat.msg.stopModelWait"));
            }
            else AppendMessage(NovaLocale.T("chat.role.system"), NovaLocale.T("chat.msg.stoppedByUser"));
            SetRunning(false, NovaLocale.T("status.stopped"));
        }

        private void StartTurn()
        {
            _turnCalls.Clear();
            _turnText = new StringBuilder();
            _reasonText = new StringBuilder();
            _reasoningLabel = null;                 // ilk düşünce parçası gelince oluşturulur
            _streamingLabel = AppendMessage("Nova", "");
            SetRunning(true, NovaLocale.T("status.thinking"));

            _cts = new CancellationTokenSource();
            var client = new BackendClient(_baseUrl, UnityAIConfig.ApiToken);
            var snapshot = new List<BackendClient.Message>(_history);
            string model = _model != null ? _model.value : ActiveModel;
            bool council = _council != null && _council.value;
            _ = client.StreamChatAsync(model, snapshot, council, ev => _events.Enqueue(ev), _cts.Token);
        }

        private void DrainEvents()
        {
            if (_messages == null) return; // pencere tam kurulmadıysa dokunma

            // 3D üretim takılırsa kullanıcı sonsuza kadar kilitli kalmasın
            if (_pendingModelCallId != null &&
                EditorApplication.timeSinceStartup - _modelStartedAt > ModelTimeoutSecs)
                FinishChatModelGeneration(false, "zaman aşımı (sunucu yanıt vermedi)");

            while (_events.TryDequeue(out var ev))
            {
                string type = ev.TryGetValue("type", out var t) ? t?.ToString() : "";
                switch (type)
                {
                    case "token":
                        if (ev.TryGetValue("text", out var tx))
                        {
                            _turnText?.Append(tx?.ToString());
                            if (_streamingLabel != null) _streamingLabel.text = _turnText.ToString();
                            ScrollToBottom();
                        }
                        break;
                    case "reasoning":
                        // Modelin düşünmesi: cevaptan ayrı, soluk bir blokta CANLI akar.
                        if (ev.TryGetValue("text", out var rt))
                        {
                            _reasonText?.Append(rt?.ToString());
                            if (_reasoningLabel == null)
                            {
                                _reasoningLabel = AppendMessage(NovaLocale.T("chat.role.thinking"), "");
                                _reasoningLabel.style.opacity = 0.55f;
                                _reasoningLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                                // Düşünce bloğu her zaman cevabın ÜSTÜNDE dursun
                                if (_streamingLabel != null && _streamingLabel.parent != null)
                                    _streamingLabel.PlaceInFront(_reasoningLabel);
                            }
                            _reasoningLabel.text = _reasonText.ToString();
                            SetStatus(NovaLocale.T("status.thinking"));
                            ScrollToBottom();
                        }
                        break;
                    case "vision":
                        // Görsel okuma adımı — kullanıcı beklerken ne olduğunu görsün
                        SetStatus(ev.TryGetValue("text", out var vt) ? vt?.ToString() : NovaLocale.T("status.thinking"));
                        break;
                    case "tool_call": RecordToolCall(ev); break;
                    case "council":
                        AppendMessage(NovaLocale.T("chat.role.auditor"), $"[{(ev.TryGetValue("verdict", out var vd) ? vd : "?")}] {(ev.TryGetValue("notes", out var nt) ? nt : "")}");
                        break;
                    case "billing":
                        if (ev.TryGetValue("totalUsd", out var c) && double.TryParse(c?.ToString(), out var usd))
                        { _totalCost += usd; UpdateCost(); }
                        break;
                    case "error":
                        AppendMessage(NovaLocale.T("chat.role.error"), ev.TryGetValue("message", out var m) ? m?.ToString() : NovaLocale.T("chat.msg.unknown"));
                        SetRunning(false, NovaLocale.T("status.error"));
                        break;
                    case "done": FinishTurn(); break;
                }
            }
        }

        private void RecordToolCall(Dictionary<string, object> ev)
        {
            var args = ev.TryGetValue("args", out var a) ? a as Dictionary<string, object> : null;
            _turnCalls.Add(new PendingCall
            {
                Id = ev.TryGetValue("id", out var id) ? id?.ToString() : "",
                Name = ev.TryGetValue("name", out var n) ? n?.ToString() : "",
                Args = args ?? new Dictionary<string, object>(),
                ArgsJson = args != null ? Json.Serialize(args) : "{}",
            });
        }

        private void FinishTurn()
        {
            string text = _turnText?.ToString().Trim() ?? "";
            if (_turnCalls.Count == 0)
            {
                if (!string.IsNullOrEmpty(text))
                    _history.Add(new BackendClient.Message { Role = "assistant", Content = text });
                SetRunning(false, "Hazır");
                return;
            }

            var toolCalls = new List<BackendClient.ToolCall>();
            foreach (var c in _turnCalls)
                toolCalls.Add(new BackendClient.ToolCall { Id = c.Id, Name = c.Name, ArgsJson = c.ArgsJson });
            _history.Add(new BackendClient.Message { Role = "assistant", Content = text, ToolCalls = toolCalls });

            foreach (var c in _turnCalls)
            {
                // ---- Nova kullanıcıya SORU soruyor: tur burada durur, cevabı beklenir ----
                if (c.Name == "AskUser")
                {
                    string q = c.Args.TryGetValue("question", out var qv) ? qv?.ToString() : "?";
                    string opts = "";
                    if (c.Args.TryGetValue("options", out var ov) && ov is List<object> list && list.Count > 0)
                        opts = "\n" + NovaLocale.T("chat.msg.optionsPrefix") + string.Join(" · ", list.ConvertAll(o => o?.ToString()));
                    AppendMessage(NovaLocale.T("chat.role.asking"), "❓ " + q + opts);
                    _pendingAskId = c.Id;
                    SetRunning(false, NovaLocale.T("status.waitingAnswer"));
                    if (_input != null) _input.Focus();
                    return;
                }

                // ONAY POLİTİKASI:
                // - WriteScript zaten Kod sekmesinde diff olarak onaya düşüyor → ikinci kez sorma.
                // - Onay gerekiyorsa KISA özet göster; dosyanın tamamını diyaloğa basma.
                // - Otomatik onay açıkken hiç sorma; her şey Undo (Ctrl+Z) ile geri alınabilir.
                // ---- 3D MODEL ÜRETİMİ: uzun süren iş, tur askıya alınır ----
                // Aracı ToolRegistry'ye bırakmıyoruz (o modeli doğrudan sahneye atıyor).
                // Bunun yerine Stüdyo hattını çalıştırıp kullanıcıyı ilerlemeyle bekletiyoruz.
                if (c.Name == "Generate3DModel")
                {
                    StartChatModelGeneration(c);
                    return;
                }

                bool needsApproval = ToolRegistry.IsDestructive(c.Name)
                                     && c.Name != "WriteScript"
                                     && (_autoApprove == null || !_autoApprove.value);
                if (needsApproval &&
                    !EditorUtility.DisplayDialog(NovaLocale.T("dialog.confirmAction.title"),
                        NovaLocale.T("dialog.confirmAction.body", Summarize(c)),
                        NovaLocale.T("dialog.continue"), NovaLocale.T("dialog.cancel")))
                {
                    AppendMessage(NovaLocale.T("chat.role.tool"), NovaLocale.T("chat.msg.toolRejected", c.Name));
                    _history.Add(new BackendClient.Message { Role = "tool", ToolCallId = c.Id,
                        Content = Json.Serialize(new Dictionary<string, object> { { "ok", false }, { "message", NovaLocale.T("chat.msg.userRejected") } }) });
                    continue;
                }
                var result = ToolRegistry.Execute(c.Name, c.Args);
                AppendMessage(NovaLocale.T("chat.role.tool"), NovaLocale.T("chat.msg.toolResultLine", result.Ok ? "✓" : "✗", c.Name, result.Message));
                _history.Add(new BackendClient.Message { Role = "tool", ToolCallId = c.Id,
                    Content = Json.Serialize(new Dictionary<string, object> { { "ok", result.Ok }, { "message", result.Message }, { "data", result.Data } }) });
            }

            if (++_turnGuard >= MaxTurns)
            {
                AppendMessage("Nova", NovaLocale.T("chat.msg.turnLimit"));
                SetRunning(false, NovaLocale.T("status.stoppedStepLimit"));
                return;
            }
            StartTurn();
        }

        private void NewChat()
        {
            _history.Clear();
            _messages?.Clear();
            _emptyHint = null;
            ShowEmptyHint();
            _turnCalls.Clear();
            _totalCost = 0;
            UpdateCost();
            _pendingAskId = null;
            NovaChatState.Clear();
            SetRunning(false, NovaLocale.T("status.newSession"));
            SwitchTab("chat");
        }

        private Label AppendMessage(string sender, string body)
        {
            HideEmptyHint();
            var container = new VisualElement();
            container.AddToClassList("msg");
            container.AddToClassList(RoleClass(sender));

            // Başlık satırı: gönderen + "kopyala" düğmesi
            var headRow = new VisualElement();
            headRow.AddToClassList("msg-head");
            var head = new Label(sender); head.AddToClassList("msg-sender");
            headRow.Add(head);

            var content = new Label(body);
            content.AddToClassList("msg-body");
            content.enableRichText = false;
            // Fareyle sürükleyerek seçip Ctrl+C ile kopyalanabilir
            content.selection.isSelectable = true;

            var copyBtn = new Button(() =>
            {
                EditorGUIUtility.systemCopyBuffer = content.text ?? "";
                SetStatus(NovaLocale.T("status.copied"));
            }) { text = "📋", tooltip = NovaLocale.T("tooltip.copyMessage") };
            copyBtn.AddToClassList("msg-copy");
            headRow.Add(copyBtn);

            container.Add(headRow); container.Add(content);
            _messages?.Add(container);
            // Yumuşak beliriş animasyonu (bir sonraki karede sınıf eklenince transition tetiklenir)
            container.schedule.Execute(() => container.AddToClassList("msg-in")).StartingIn(16);
            ScrollToBottom();
            return content;
        }

        // ═══════════ SOHBETTEN 3D MODEL ÜRETİMİ ═══════════
        // Kullanıcı "bana bir kılıç modeli yap" der; ajan promptu düzenler, burada Stüdyo
        // hattı çalışır, kullanıcı ilerlemeyi görerek bekler, model gelince ajan kapanış
        // mesajını yazar ve kullanıcı Stüdyo'ya yönlendirilir.

        private void StartChatModelGeneration(PendingCall c)
        {
            string prompt = c.Args.TryGetValue("prompt", out var p) ? p?.ToString() : null;
            string name = c.Args.TryGetValue("name", out var n) ? n?.ToString() : null;
            if (string.IsNullOrWhiteSpace(name)) name = "GeneratedModel";

            if (string.IsNullOrWhiteSpace(prompt))
            {
                AddToolResult(c.Id, false, NovaLocale.T("gen3d.emptyPrompt"));
                StartTurn();
                return;
            }

            // 3D üretim ücretli bir iştir: otomatik onay kapalıysa tek satırlık onay al.
            if ((_autoApprove == null || !_autoApprove.value) &&
                !EditorUtility.DisplayDialog(NovaLocale.T("dialog.genModel.title"),
                    NovaLocale.T("dialog.genModel3D.body", prompt),
                    NovaLocale.T("dialog.generate"), NovaLocale.T("dialog.cancel")))
            {
                AppendMessage(NovaLocale.T("chat.role.tool"), NovaLocale.T("chat.msg.toolRejected", "Generate3DModel"));
                AddToolResult(c.Id, false, NovaLocale.T("gen3d.userRejected"));
                StartTurn();
                return;
            }

            _pendingModelCallId = c.Id;
            _pendingModelName = name;
            _modelStartedAt = EditorApplication.timeSinceStartup;

            // Stüdyo alanlarını da doldur: kullanıcı sekmeye geçince aynı promptu görsün
            if (_studioPrompt != null) _studioPrompt.value = prompt;
            if (_studioMode != null) _studioMode.index = 0;
            UpdateStudioMode();
            _lastPrompt = prompt;
            _isRigged = false; _walkUrl = null;

            _modelProgressLabel = AppendMessage("Nova", NovaLocale.T("gen3d.inProgress", name));
            SetRunning(true, NovaLocale.T("gen3d.statusGenerating"));

            int faceLimit = _studioQuality == null ? 0
                : (_studioQuality.index == 2 ? 12000 : (_studioQuality.index == 1 ? 30000 : 0));
            StartGenProgress();
            ModelGenerator.GeneratePreview(
                _baseUrl, UnityAIConfig.ApiToken, prompt, null, name, faceLimit,
                OnPreviewReady, OnGenStep,
                msg => { if (_studioStatus != null) _studioStatus.text = msg; });
        }

        /// <summary>Üretim bitti (ya da patladı): tool sonucunu geçmişe yaz, turu sürdür.</summary>
        private void FinishChatModelGeneration(bool ok, string error)
        {
            if (_pendingModelCallId == null) return;
            string id = _pendingModelCallId;
            _pendingModelCallId = null;
            double secs = EditorApplication.timeSinceStartup - _modelStartedAt;

            if (_modelProgressLabel != null)
                _modelProgressLabel.text = ok
                    ? NovaLocale.T("gen3d.ready", _pendingModelName, secs)
                    : NovaLocale.T("gen3d.failed", error);

            if (ok) FlagStudioReady();

            AddToolResult(id, ok, ok
                ? NovaLocale.T("gen3d.readyToolResult", _pendingModelName)
                : NovaLocale.T("gen3d.failedToolResult", error));

            StartTurn();   // ajan kapanış mesajını yazsın
        }

        private void AddToolResult(string callId, bool ok, string message)
        {
            _history.Add(new BackendClient.Message
            {
                Role = "tool",
                ToolCallId = callId,
                Content = Json.Serialize(new Dictionary<string, object> { { "ok", ok }, { "message", message } }),
            });
        }

        /// <summary>Stüdyo sekmesine "hazır" işareti koyar; kullanıcı sekmeye girince kalkar.</summary>
        private void FlagStudioReady()
        {
            if (_tabStudio == null) return;
            if (!_tabStudio.text.EndsWith(" ●")) _tabStudio.text += " ●";
            _tabStudio.tooltip = NovaLocale.T("tooltip.newModelReady");
        }

        private void ClearStudioFlag()
        {
            if (_tabStudio == null) return;
            if (_tabStudio.text.EndsWith(" ●"))
                _tabStudio.text = _tabStudio.text.Substring(0, _tabStudio.text.Length - 2);
            _tabStudio.tooltip = null;
        }

        /// <summary>Onay diyaloğu için insan diliyle KISA özet (dosya içeriği asla basılmaz).</summary>
        private static string Summarize(PendingCall c)
        {
            string A(string k) => c.Args != null && c.Args.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
            switch (c.Name)
            {
                case "BuildTerrain":
                    return $"Araziyi yeniden kuracak: {A("biome")} · {A("size")} m";
                case "RemovePlacedAssets":
                    return $"Sahneden asset kaldıracak (eşleşme: '{A("match")}{A("role")}')";
                case "DeleteGameObject":
                    return $"'{A("name")}' nesnesini silecek";
                case "Generate3DModel":
                    return $"3D model üretecek: {A("prompt")}";
                default:
                    var parts = new List<string>();
                    if (c.Args != null)
                        foreach (var kv in c.Args)
                        {
                            string v = kv.Value?.ToString() ?? "";
                            if (v.Length > 60) v = v.Substring(0, 60) + $"… ({v.Length} karakter)";
                            parts.Add($"{kv.Key}: {v}");
                        }
                    return $"{c.Name}\n" + string.Join("\n", parts);
            }
        }

        private static string RoleClass(string sender)
        {
            if (sender == "Sen") return "msg-user";
            if (sender == "Nova") return "msg-nova";
            return "msg-tool";
        }

        // ---- Tema (açık / koyu) ----
        private void ApplyTheme(string theme)
        {
            bool light = theme == "light";
            if (_rootEl != null)
            {
                if (light) _rootEl.AddToClassList("light");
                else _rootEl.RemoveFromClassList("light");
            }
            if (_themeBtn != null) _themeBtn.text = NovaLocale.T(light ? "app.theme.light" : "app.theme.dark");
            EditorPrefs.SetString("UnityAI.Theme", theme);
        }

        private void ToggleTheme()
        {
            bool isLight = _rootEl != null && _rootEl.ClassListContains("light");
            ApplyTheme(isLight ? "dark" : "light");
        }

        private void ScrollToBottom()
        {
            if (_messages != null) _messages.scrollOffset = new Vector2(0, float.MaxValue);
        }

        private void SetStatus(string s) { if (_status != null) _status.text = s; }
        private void SetEditEnabled(bool on) { } // düzenleme UI kaldırıldı; no-op

        private void UpdateCost() { if (_cost != null) _cost.text = $"${_totalCost:0.0000}"; }
    }
}
