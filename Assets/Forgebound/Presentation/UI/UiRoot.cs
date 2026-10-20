using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Forgebound.Presentation
{
    /// <summary>Base for runtime-built screens. Screens are plain objects with a root RectTransform.</summary>
    public abstract class UiScreen
    {
        protected Services S;
        public RectTransform Root;
        public virtual bool ShowsBattleWorld { get { return false; } }

        public void Attach(Services s, RectTransform root)
        {
            S = s;
            Root = root;
            Build();
        }

        protected abstract void Build();
        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual void Tick(float dt) { }
        /// <summary>Android back button / Escape. Return true when handled.</summary>
        public virtual bool OnBack() { return false; }
        protected string L(string key) { return S.Loc.Get(key); }
        protected string LF(string key, params object[] args) { return S.Loc.Format(key, args); }
    }

    /// <summary>
    /// Canvas owner: screen navigation, safe area, tooltip/modal/toast layers. Reference resolution 1920x1080,
    /// scaled with screen (match 0.5) so the same layout works on 16:9 desktop, tall phones and 4:3 tablets.
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        public static UiRoot Instance { get; private set; }
        private Services _s;
        private RectTransform _safe, _screenLayer, _overlayLayer;
        private UiScreen _current;
        private readonly Stack<Func<UiScreen>> _history = new Stack<Func<UiScreen>>();
        private Func<UiScreen> _currentFactory;
        private Image _tooltip;
        private Text _tooltipText;
        private RectTransform _toast;
        private float _toastUntil;
        private Rect _lastSafe;
        public Canvas Canvas { get; private set; }

        public static UiRoot Create(Services s)
        {
            var go = new GameObject("UiRoot");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                DontDestroyOnLoad(es);
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            var root = go.AddComponent<UiRoot>();
            root.Canvas = canvas;
            root._s = s;
            root.BuildLayers();
            Instance = root;
            return root;
        }

        private void BuildLayers()
        {
            _safe = UiKit.Rect(transform, "SafeArea");
            UiKit.Stretch(_safe);
            _screenLayer = UiKit.Rect(_safe, "Screens");
            UiKit.Stretch(_screenLayer);
            _overlayLayer = UiKit.Rect(_safe, "Overlay");
            UiKit.Stretch(_overlayLayer);
            _tooltip = UiKit.Panel(_overlayLayer, "Tooltip", new Color(0.08f, 0.06f, 0.05f, 0.96f));
            _tooltip.raycastTarget = false;
            _tooltip.rectTransform.pivot = new Vector2(0, 1);
            _tooltip.rectTransform.anchorMin = _tooltip.rectTransform.anchorMax = Vector2.zero;
            _tooltip.rectTransform.sizeDelta = new Vector2(560, 200);
            _tooltipText = UiKit.Label(_tooltip.transform, "", 22, Theme.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(_tooltipText.rectTransform, 14, 10, 14, 10);
            _tooltip.gameObject.SetActive(false);
            ApplySafeArea();
        }

        private void ApplySafeArea()
        {
            var sa = Screen.safeArea;
            if (sa == _lastSafe || Screen.width <= 0 || Screen.height <= 0) return;
            _lastSafe = sa;
            _safe.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            _safe.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            _safe.offsetMin = Vector2.zero;
            _safe.offsetMax = Vector2.zero;
        }

        public UiScreen Current { get { return _current; } }

        /// <summary>Replace the current screen. `remember` pushes the previous one for Back navigation.</summary>
        public void Show(Func<UiScreen> factory, bool remember = false)
        {
            if (_current != null)
            {
                _current.OnHide();
                Destroy(_current.Root.gameObject);
                if (remember && _currentFactory != null) _history.Push(_currentFactory);
            }
            if (!remember) _history.Clear();
            HideTooltip();
            var screen = factory();
            var root = UiKit.Rect(_screenLayer, screen.GetType().Name);
            UiKit.Stretch(root);
            screen.Attach(_s, root);
            _current = screen;
            _currentFactory = factory;
            screen.OnShow();
            if (BattleWorld.Instance != null) BattleWorld.Instance.SetVisible(screen.ShowsBattleWorld);
        }

        public bool Back()
        {
            if (_history.Count == 0) return false;
            var f = _history.Pop();
            var saved = _history.ToArray();   // top of stack first
            Show(f, false);
            for (int i = saved.Length - 1; i >= 0; i--) _history.Push(saved[i]);
            return true;
        }

        public void ShowTooltip(string text, Vector2 screenPos)
        {
            if (string.IsNullOrEmpty(text)) return;
            _tooltipText.text = text;
            _tooltip.gameObject.SetActive(true);
            _tooltip.transform.SetAsLastSibling();
            int lines = 1;
            foreach (var c in text) if (c == '\n') lines++;
            float h = Mathf.Clamp(28 * UiKit.TextScale * lines + 30, 70, 700);
            _tooltip.rectTransform.sizeDelta = new Vector2(600, h);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_overlayLayer, screenPos, null, out var local);
            var size = _overlayLayer.rect.size;
            float x = local.x + size.x * 0.5f + 24;
            float y = local.y + size.y * 0.5f - 24;
            if (x + 600 > size.x) x = size.x - 610;
            if (y - h < 0) y = h + 10;
            _tooltip.rectTransform.anchoredPosition = new Vector2(x, y);
        }

        public void HideTooltip() { if (_tooltip != null) _tooltip.gameObject.SetActive(false); }

        public void Toast(string text, float seconds = 2.5f)
        {
            if (_toast != null) Destroy(_toast.gameObject);
            var img = UiKit.Panel(_overlayLayer, "Toast", new Color(0.1f, 0.08f, 0.07f, 0.95f));
            _toast = img.rectTransform;
            _toast.anchorMin = new Vector2(0.25f, 0.88f);
            _toast.anchorMax = new Vector2(0.75f, 0.96f);
            _toast.offsetMin = _toast.offsetMax = Vector2.zero;
            var t = UiKit.Label(img.transform, text, 26, Theme.Gold, TextAnchor.MiddleCenter);
            UiKit.Stretch(t.rectTransform, 12, 6, 12, 6);
            _toastUntil = Time.unscaledTime + seconds;
        }

        /// <summary>Modal confirmation for destructive / irreversible actions (sell, abandon, overwrite).</summary>
        public void Confirm(string message, string yes, string no, Action onYes)
        {
            var blocker = UiKit.Panel(_overlayLayer, "Modal", new Color(0, 0, 0, 0.7f));
            UiKit.Stretch(blocker.rectTransform);
            var box = UiKit.Panel(blocker.transform, "Box", Theme.Panel);
            UiKit.Anchor(box.rectTransform, 0.3f, 0.35f, 0.7f, 0.65f);
            var v = UiKit.VBox(box.transform, "V", 16, 24);
            UiKit.Stretch((RectTransform)v.transform);
            var msg = UiKit.Label(v.transform, message, 28, Theme.Text, TextAnchor.MiddleCenter);
            UiKit.Size(msg, -1, 120, -1, -1, -1, 1);
            var h = UiKit.HBox(v.transform, "Buttons", 16);
            UiKit.Size(h, -1, UiKit.MinTouch);
            UiKit.Button(h.transform, no, () => Destroy(blocker.gameObject));
            UiKit.Button(h.transform, yes, () => { Destroy(blocker.gameObject); onYes(); }, Theme.Danger);
        }

        private void Update()
        {
            ApplySafeArea();
            float dt = Time.unscaledDeltaTime;
            if (_current != null) _current.Tick(dt);
            if (_toast != null && Time.unscaledTime > _toastUntil) { Destroy(_toast.gameObject); _toast = null; }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Android back maps to Escape.
                if (_current == null || !_current.OnBack()) Back();
            }
        }
    }
}
