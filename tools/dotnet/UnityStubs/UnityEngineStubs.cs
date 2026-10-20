// Compile-only stand-ins for the subset of the Unity 2022.3 API used by Assets/Forgebound/Presentation.
// They mirror real signatures so type/syntax errors surface without a Unity install. They do NOT validate runtime
// behaviour or prove the code works in Unity — see docs/KNOWN_ISSUES.md ("Unity verification pending").
#pragma warning disable CS0067, CS0649, CS0414
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object o) { }
        public static void DontDestroyOnLoad(Object o) { }
        public static T FindObjectOfType<T>() where T : Object { return null; }
        public static implicit operator bool(Object o) { return !ReferenceEquals(o, null); }
    }

    public class Component : Object
    {
        public GameObject gameObject { get { return null; } }
        public Transform transform { get { return null; } }
        public T GetComponent<T>() { return default(T); }
        public T GetComponentInChildren<T>() { return default(T); }
    }

    public class Behaviour : Component { public bool enabled { get; set; } }

    public class MonoBehaviour : Behaviour { }

    public sealed class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public GameObject(string name, params Type[] components) { }
        public Transform transform { get { return null; } }
        public T AddComponent<T>() where T : Component { return null; }
        public T GetComponent<T>() { return default(T); }
        public void SetActive(bool v) { }
        public bool activeSelf { get { return true; } }
    }

    public class Transform : Component, IEnumerable
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public Quaternion localRotation { get; set; }
        public Quaternion rotation { get; set; }
        public Transform parent { get; set; }
        public int childCount { get { return 0; } }
        public Transform GetChild(int i) { return null; }
        public void SetParent(Transform p, bool worldPositionStays) { }
        public void SetParent(Transform p) { }
        public void SetAsLastSibling() { }
        public void SetAsFirstSibling() { }
        public IEnumerator GetEnumerator() { yield break; }
    }

    public sealed class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Rect rect { get { return default(Rect); } }
    }

    public static class RectTransformUtility
    {
        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector2 localPoint) { localPoint = default(Vector2); return true; }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(0, 0); } }
        public static Vector2 one { get { return new Vector2(1, 1); } }
        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y); } }
        public static Vector2 Min(Vector2 a, Vector2 b) { return a; }
        public static Vector2 Max(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator *(Vector2 a, float d) { return a; }
        public static Vector2 operator *(float d, Vector2 a) { return a; }
        public static Vector2 operator /(Vector2 a, float d) { return a; }
        public static bool operator ==(Vector2 a, Vector2 b) { return true; }
        public static bool operator !=(Vector2 a, Vector2 b) { return false; }
        public override bool Equals(object o) { return true; }
        public override int GetHashCode() { return 0; }
        public static implicit operator Vector3(Vector2 v) { return new Vector3(v.x, v.y, 0); }
        public static implicit operator Vector2(Vector3 v) { return new Vector2(v.x, v.y); }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; }
        public static Vector3 zero { get { return default(Vector3); } }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public float magnitude { get { return 0; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator *(Vector3 a, float d) { return a; }
        public static Vector3 operator *(float d, Vector3 a) { return a; }
        public static Vector3 operator /(Vector3 a, float d) { return a; }
    }

    public struct Quaternion
    {
        public static Quaternion identity { get { return default(Quaternion); } }
        public static Quaternion Euler(float x, float y, float z) { return default(Quaternion); }
    }

    public struct Rect
    {
        public Rect(float x, float y, float w, float h) { xMin = x; yMin = y; xMax = x + w; yMax = y + h; }
        public float xMin, yMin, xMax, yMax;
        public Vector2 size { get { return default(Vector2); } }
        public static bool operator ==(Rect a, Rect b) { return true; }
        public static bool operator !=(Rect a, Rect b) { return false; }
        public override bool Equals(object o) { return true; }
        public override int GetHashCode() { return 0; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1; }
        public static Color white { get { return default(Color); } }
        public static Color black { get { return default(Color); } }
        public static Color gray { get { return default(Color); } }
        public static Color magenta { get { return default(Color); } }
        public static Color clear { get { return default(Color); } }
        public static Color Lerp(Color a, Color b, float t) { return a; }
        public static Color operator *(Color a, float d) { return a; }
        public static Color operator +(Color a, Color b) { return a; }
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class ColorUtility
    {
        public static bool TryParseHtmlString(string s, out Color c) { c = default(Color); return true; }
        public static string ToHtmlStringRGB(Color c) { return "ffffff"; }
    }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public const float Rad2Deg = 57.29578f;
        public static float Clamp01(float v) { return v; }
        public static float Clamp(float v, float a, float b) { return v; }
        public static int Clamp(int v, int a, int b) { return v; }
        public static float Lerp(float a, float b, float t) { return a; }
        public static float Sin(float v) { return 0; }
        public static float Cos(float v) { return 0; }
        public static float Abs(float v) { return v; }
        public static float Sqrt(float v) { return v; }
        public static float Pow(float a, float b) { return a; }
        public static float Max(float a, float b) { return a; }
        public static int Max(int a, int b) { return a; }
        public static float Min(float a, float b) { return a; }
        public static int Min(int a, int b) { return a; }
        public static float Atan2(float y, float x) { return 0; }
        public static int FloorToInt(float v) { return 0; }
        public static int RoundToInt(float v) { return 0; }
        public static float MoveTowards(float a, float b, float d) { return a; }
    }

    public static class Random
    {
        public static float Range(float a, float b) { return a; }
        public static int Range(int a, int b) { return a; }
        public static Vector2 insideUnitCircle { get { return default(Vector2); } }
    }

    public static class Time
    {
        public static float time { get { return 0; } }
        public static float deltaTime { get { return 0; } }
        public static float unscaledTime { get { return 0; } }
        public static float unscaledDeltaTime { get { return 0; } }
    }

    public enum KeyCode { None, Escape, Space, Alpha1, Alpha2, R, Z, Y, Delete, LeftControl, LeftCommand }

    public static class Input
    {
        public static bool GetKeyDown(KeyCode k) { return false; }
        public static bool GetKey(KeyCode k) { return false; }
        public static int touchCount { get { return 0; } }
        public static bool anyKeyDown { get { return false; } }
        public static bool multiTouchEnabled { get; set; }
    }

    public static class Screen
    {
        public static int width { get { return 1920; } }
        public static int height { get { return 1080; } }
        public static Rect safeArea { get { return default(Rect); } }
    }

    public enum RuntimePlatform { WindowsPlayer, Android, IPhonePlayer, OSXEditor, WindowsEditor, LinuxEditor }

    public static class Application
    {
        public static int targetFrameRate { get; set; }
        public static RuntimePlatform platform { get { return RuntimePlatform.WindowsPlayer; } }
        public static string version { get { return "0"; } }
        public static string persistentDataPath { get { return ""; } }
        public static void Quit() { }
    }

    public static class QualitySettings { public static int vSyncCount { get; set; } }

    public static class Debug
    {
        public static void Log(object o) { }
        public static void LogWarning(object o) { }
        public static void LogError(object o) { }
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object { return null; }
        public static T[] LoadAll<T>(string path) where T : Object { return new T[0]; }
        public static T GetBuiltinResource<T>(string path) where T : Object { return null; }
    }

    public class TextAsset : Object { public string text { get { return ""; } } }
    public class Material : Object { }
    public class Font : Object { public Material material { get { return null; } } }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }

    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Repeat, Clamp }
    public enum FilterMode { Point, Bilinear, Trilinear }

    public class Texture : Object
    {
        public TextureWrapMode wrapMode { get; set; }
        public FilterMode filterMode { get; set; }
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int w, int h, TextureFormat f, bool mip) { }
        public void SetPixels32(Color32[] px) { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
    }

    public sealed class Sprite : Object
    {
        public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu) { return null; }
    }

    public class Renderer : Component
    {
        public int sortingOrder { get; set; }
        public Material material { get; set; }
        public bool enabled { get; set; }
    }

    public sealed class SpriteRenderer : Renderer
    {
        public Sprite sprite { get; set; }
        public Color color { get; set; }
        public bool flipX { get; set; }
    }

    public sealed class MeshRenderer : Renderer { }

    public sealed class TextMesh : Component
    {
        public Font font { get; set; }
        public string text { get; set; }
        public Color color { get; set; }
        public float characterSize { get; set; }
        public int fontSize { get; set; }
        public TextAnchor anchor { get; set; }
        public FontStyle fontStyle { get; set; }
        public bool richText { get; set; }
    }

    public enum CameraClearFlags { Skybox, SolidColor, Depth, Nothing }

    public sealed class Camera : Behaviour
    {
        public bool orthographic { get; set; }
        public float orthographicSize { get; set; }
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public float depth { get; set; }
    }

    public sealed class AudioClip : Object { }

    public sealed class AudioSource : Behaviour
    {
        public bool playOnAwake { get; set; }
        public bool loop { get; set; }
        public bool isPlaying { get { return false; } }
        public AudioClip clip { get; set; }
        public float volume { get; set; }
        public float pitch { get; set; }
        public float time { get; set; }
        public void Play() { }
        public void Stop() { }
    }

    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { }
    }

    public static class GUIUtility { public static string systemCopyBuffer { get; set; } }

    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }

    public sealed class Canvas : Behaviour
    {
        public RenderMode renderMode { get; set; }
        public int sortingOrder { get; set; }
    }

    public class RectOffset
    {
        public RectOffset(int left, int right, int top, int bottom) { }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public class UnityEvent
    {
        public void AddListener(UnityAction a) { }
        public void RemoveAllListeners() { }
    }
}

namespace UnityEngine.EventSystems
{
    public class UIBehaviour : MonoBehaviour { }
    public class EventSystem : UIBehaviour { }
    public class BaseInputModule : UIBehaviour { }
    public class StandaloneInputModule : BaseInputModule { }

    public class PointerEventData
    {
        public Vector2 position { get; set; }
        public Camera pressEventCamera { get { return null; } }
        public bool dragging { get; set; }
    }

    public interface IEventSystemHandler { }
    public interface IPointerEnterHandler : IEventSystemHandler { void OnPointerEnter(PointerEventData e); }
    public interface IPointerExitHandler : IEventSystemHandler { void OnPointerExit(PointerEventData e); }
    public interface IPointerDownHandler : IEventSystemHandler { void OnPointerDown(PointerEventData e); }
    public interface IPointerUpHandler : IEventSystemHandler { void OnPointerUp(PointerEventData e); }
    public interface IPointerClickHandler : IEventSystemHandler { void OnPointerClick(PointerEventData e); }
    public interface IBeginDragHandler : IEventSystemHandler { void OnBeginDrag(PointerEventData e); }
    public interface IDragHandler : IEventSystemHandler { void OnDrag(PointerEventData e); }
    public interface IEndDragHandler : IEventSystemHandler { void OnEndDrag(PointerEventData e); }
}

namespace UnityEngine.UI
{
    using UnityEngine.EventSystems;

    public class Graphic : UIBehaviour
    {
        public Color color { get; set; }
        public bool raycastTarget { get; set; }
        public RectTransform rectTransform { get { return null; } }
    }

    public class MaskableGraphic : Graphic { }

    public class Image : MaskableGraphic
    {
        public enum Type { Simple, Sliced, Tiled, Filled }
        public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
        public Sprite sprite { get; set; }
        public bool preserveAspect { get; set; }
        public Type type { get; set; }
        public FillMethod fillMethod { get; set; }
        public float fillAmount { get; set; }
    }

    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }

    public class Text : MaskableGraphic
    {
        public Font font { get; set; }
        public string text { get; set; }
        public int fontSize { get; set; }
        public TextAnchor alignment { get; set; }
        public FontStyle fontStyle { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
        public bool resizeTextForBestFit { get; set; }
        public int resizeTextMinSize { get; set; }
        public int resizeTextMaxSize { get; set; }
        public bool supportRichText { get; set; }
    }

    public struct ColorBlock
    {
        public Color normalColor { get; set; }
        public Color highlightedColor { get; set; }
        public Color pressedColor { get; set; }
        public Color disabledColor { get; set; }
    }

    public class Selectable : UIBehaviour
    {
        public enum Transition { None, ColorTint, SpriteSwap, Animation }
        public ColorBlock colors { get; set; }
        public Graphic targetGraphic { get; set; }
        public bool interactable { get; set; }
        public Transition transition { get; set; }
    }

    public class Button : Selectable
    {
        public class ButtonClickedEvent : UnityEngine.Events.UnityEvent { }
        public ButtonClickedEvent onClick { get { return null; } }
    }

    public class InputField : Selectable
    {
        public enum ContentType { Standard, IntegerNumber, DecimalNumber }
        public Text textComponent { get; set; }
        public Graphic placeholder { get; set; }
        public ContentType contentType { get; set; }
        public string text { get; set; }
    }

    public class LayoutElement : UIBehaviour
    {
        public float minWidth { get; set; }
        public float minHeight { get; set; }
        public float preferredWidth { get; set; }
        public float preferredHeight { get; set; }
        public float flexibleWidth { get; set; }
        public float flexibleHeight { get; set; }
    }

    public abstract class LayoutGroup : UIBehaviour { public RectOffset padding { get; set; } }

    public abstract class HorizontalOrVerticalLayoutGroup : LayoutGroup
    {
        public float spacing { get; set; }
        public bool childControlHeight { get; set; }
        public bool childControlWidth { get; set; }
        public bool childForceExpandHeight { get; set; }
        public bool childForceExpandWidth { get; set; }
    }

    public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup { }

    public class GridLayoutGroup : LayoutGroup
    {
        public Vector2 cellSize { get; set; }
        public Vector2 spacing { get; set; }
    }

    public class ContentSizeFitter : UIBehaviour
    {
        public enum FitMode { Unconstrained, MinSize, PreferredSize }
        public FitMode verticalFit { get; set; }
        public FitMode horizontalFit { get; set; }
    }

    public class ScrollRect : UIBehaviour
    {
        public bool horizontal { get; set; }
        public bool vertical { get; set; }
        public float scrollSensitivity { get; set; }
        public RectTransform viewport { get; set; }
        public RectTransform content { get; set; }
    }

    public class RectMask2D : UIBehaviour { }
    public class GraphicRaycaster : UIBehaviour { }

    public class CanvasScaler : UIBehaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public ScreenMatchMode screenMatchMode { get; set; }
        public float matchWidthOrHeight { get; set; }
    }
}
