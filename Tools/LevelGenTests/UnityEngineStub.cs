// A managed stand-in for the parts of UnityEngine that the level generator's Core and its tests use, so they build and
// run outside the Editor (see README.md). The tests run on the Editor's Mono and must generate the levels the Editor
// generates, so every member here is written as Unity's managed source writes it (per component, the same constants):
// the same expression in another shape rounds differently. Quaternion.Euler is native in Unity; this one matches it for
// yaw-only rotations.
using System;
namespace UnityEngine {
  public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) {} }
  public class TooltipAttribute : Attribute { public TooltipAttribute(string s) {} }
  public class HeaderAttribute : Attribute { public HeaderAttribute(string s) {} }
  public struct LayerMask { public int value; }
  public class MinAttribute : Attribute { public MinAttribute(float a) {} }
  public class SerializeField : Attribute {}
  public static class Debug { public static void Log(object o) {} public static void LogWarning(object o) {} public static void LogError(object o) {} }
  public static class Mathf {
    // Defined as Unity defines them: 180f / PI is a bit off Unity's Rad2Deg.
    public const float PI = (float)Math.PI, Deg2Rad = PI * 2F / 360F, Rad2Deg = 1F / Deg2Rad, Infinity = float.PositiveInfinity, Epsilon = float.Epsilon;
    public static float Max(float a, float b) => a > b ? a : b; public static int Max(int a, int b) => a > b ? a : b;
    public static float Max(params float[] v) { float m = v[0]; foreach (var x in v) m = Max(m, x); return m; }
    public static float Min(float a, float b) => a < b ? a : b; public static int Min(int a, int b) => a < b ? a : b;
    public static float Min(params float[] v) { float m = v[0]; foreach (var x in v) m = Min(m, x); return m; }
    public static float Abs(float a) => Math.Abs(a); public static int Abs(int a) => Math.Abs(a);
    public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v; public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
    public static float Clamp01(float v) => Clamp(v, 0f, 1f);
    public static int FloorToInt(float f) => (int)Math.Floor(f); public static int CeilToInt(float f) => (int)Math.Ceiling(f); public static int RoundToInt(float f) => (int)Math.Round(f);
    public static float Floor(float f) => (float)Math.Floor(f); public static float Ceil(float f) => (float)Math.Ceiling(f); public static float Round(float f) => (float)Math.Round(f);
    public static float Sqrt(float f) => (float)Math.Sqrt(f); public static float Sin(float f) => (float)Math.Sin(f); public static float Cos(float f) => (float)Math.Cos(f); public static float Tan(float f) => (float)Math.Tan(f);
    public static float Atan(float f) => (float)Math.Atan(f); public static float Atan2(float y, float x) => (float)Math.Atan2(y, x); public static float Acos(float f) => (float)Math.Acos(f); public static float Asin(float f) => (float)Math.Asin(f);
    public static float Pow(float a, float b) => (float)Math.Pow(a, b); public static float Exp(float a) => (float)Math.Exp(a); public static float Log(float a) => (float)Math.Log(a);
    public static float Sign(float f) => f >= 0f ? 1f : -1f;
    public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t); public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
    public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
    public static bool Approximately(float a, float b) => Math.Abs(b - a) < Math.Max(1E-06f * Math.Max(Math.Abs(a), Math.Abs(b)), Epsilon * 8f);
    public static float Repeat(float t, float l) => Clamp(t - Floor(t / l) * l, 0f, l);
    public static float SmoothStep(float a, float b, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return b * t + a * (1f - t); }
  }
  public struct Vector2 : IEquatable<Vector2> {
    public float x, y;
    public Vector2(float x, float y) { this.x = x; this.y = y; }
    public float this[int i] { get => i == 0 ? x : y; set { if (i == 0) x = value; else y = value; } }
    public static Vector2 zero => new(0, 0); public static Vector2 one => new(1, 1); public static Vector2 right => new(1, 0); public static Vector2 left => new(-1, 0); public static Vector2 up => new(0, 1); public static Vector2 down => new(0, -1);
    public float magnitude => (float)Math.Sqrt(x * x + y * y); public float sqrMagnitude => x * x + y * y;
    public Vector2 normalized { get { float m = magnitude; return m > 1E-05f ? this / m : zero; } }
    public void Normalize() { this = normalized; }
    public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
    public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
    // Per component, as Unity writes it: under Mono's double-precision float maths, going through the operators would
    // round each step to float and give different bits.
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { t = Mathf.Clamp01(t); return new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t); }
    public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t) => new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
    public static Vector2 Min(Vector2 a, Vector2 b) => new(Math.Min(a.x, b.x), Math.Min(a.y, b.y)); public static Vector2 Max(Vector2 a, Vector2 b) => new(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
    public static Vector2 Perpendicular(Vector2 v) => new(-v.y, v.x);
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y); public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
    public static Vector2 operator -(Vector2 a) => new(-a.x, -a.y); public static Vector2 operator *(Vector2 a, float d) => new(a.x * d, a.y * d); public static Vector2 operator *(float d, Vector2 a) => new(a.x * d, a.y * d);
    public static Vector2 operator *(Vector2 a, Vector2 b) => new(a.x * b.x, a.y * b.y);
    public static Vector2 operator /(Vector2 a, float d) => new(a.x / d, a.y / d);
    public static bool operator ==(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 9.99999944E-11f; public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
    public static implicit operator Vector3(Vector2 v) => new(v.x, v.y, 0); public static implicit operator Vector2(Vector3 v) => new(v.x, v.y);
    public bool Equals(Vector2 o) => x == o.x && y == o.y; public override bool Equals(object o) => o is Vector2 v && Equals(v); public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);
    public override string ToString() => $"({x:F2}, {y:F2})"; public string ToString(string f) => $"({x.ToString(f)}, {y.ToString(f)})";
  }
  public struct Vector3 : IEquatable<Vector3> {
    public float x, y, z;
    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; }
    public float this[int i] { get => i == 0 ? x : i == 1 ? y : z; set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; } }
    public static Vector3 zero => new(0, 0, 0); public static Vector3 one => new(1, 1, 1); public static Vector3 up => new(0, 1, 0); public static Vector3 down => new(0, -1, 0); public static Vector3 right => new(1, 0, 0); public static Vector3 left => new(-1, 0, 0); public static Vector3 forward => new(0, 0, 1); public static Vector3 back => new(0, 0, -1);
    public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z); public float sqrMagnitude => x * x + y * y + z * z;
    public Vector3 normalized { get { float m = magnitude; return m > 1E-05f ? this / m : zero; } }
    public void Normalize() { this = normalized; }
    public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t); }
    public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
    public static Vector3 Min(Vector3 a, Vector3 b) => new(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z)); public static Vector3 Max(Vector3 a, Vector3 b) => new(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z));
    public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z); public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vector3 operator -(Vector3 a) => new(-a.x, -a.y, -a.z); public static Vector3 operator *(Vector3 a, float d) => new(a.x * d, a.y * d, a.z * d); public static Vector3 operator *(float d, Vector3 a) => a * d;
    public static Vector3 operator /(Vector3 a, float d) => new(a.x / d, a.y / d, a.z / d);
    public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 9.99999944E-11f; public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
    public bool Equals(Vector3 o) => x == o.x && y == o.y && z == o.z; public override bool Equals(object o) => o is Vector3 v && Equals(v); public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
    public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})"; public string ToString(string f) => $"({x.ToString(f)}, {y.ToString(f)}, {z.ToString(f)})";
  }
  public struct Vector4 : IEquatable<Vector4> {
    public float x, y, z, w;
    public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    public static Vector4 operator +(Vector4 a, Vector4 b) => new(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
    public bool Equals(Vector4 o) => x == o.x && y == o.y && z == o.z && w == o.w; public override bool Equals(object o) => o is Vector4 v && Equals(v); public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1);
    public override string ToString() => $"({x:F2}, {y:F2}, {z:F2}, {w:F2})";
  }
  public struct Quaternion {
    public float x, y, z, w;
    public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    public static Quaternion identity => new(0, 0, 0, 1);
    public static Quaternion Euler(float ex, float ey, float ez) {
      // Unity: ZXY order (z, then x, then y applied) => q = qy * qx * qz
      Quaternion Ax(float deg, float ax, float ay, float az) { float h = deg * Mathf.Deg2Rad * 0.5f; float s = (float)Math.Sin(h); return new(ax * s, ay * s, az * s, (float)Math.Cos(h)); }
      return Ax(ey, 0, 1, 0) * Ax(ex, 1, 0, 0) * Ax(ez, 0, 0, 1);
    }
    public static Quaternion Euler(Vector3 e) => Euler(e.x, e.y, e.z);
    public static Quaternion AngleAxis(float deg, Vector3 axis) { axis = axis.normalized; float h = deg * Mathf.Deg2Rad * 0.5f; float s = (float)Math.Sin(h); return new(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(h)); }
    public static Quaternion Inverse(Quaternion q) => new(-q.x, -q.y, -q.z, q.w);
    public static Quaternion operator *(Quaternion l, Quaternion r) => new(l.w * r.x + l.x * r.w + l.y * r.z - l.z * r.y, l.w * r.y + l.y * r.w + l.z * r.x - l.x * r.z, l.w * r.z + l.z * r.w + l.x * r.y - l.y * r.x, l.w * r.w - l.x * r.x - l.y * r.y - l.z * r.z);
    public static Vector3 operator *(Quaternion r, Vector3 p) {
      float x = r.x * 2F, y = r.y * 2F, z = r.z * 2F, xx = r.x * x, yy = r.y * y, zz = r.z * z, xy = r.x * y, xz = r.x * z, yz = r.y * z, wx = r.w * x, wy = r.w * y, wz = r.w * z;
      return new((1F - (yy + zz)) * p.x + (xy - wz) * p.y + (xz + wy) * p.z, (xy + wz) * p.x + (1F - (xx + zz)) * p.y + (yz - wx) * p.z, (xz - wy) * p.x + (yz + wx) * p.y + (1F - (xx + yy)) * p.z);
    }
  }
}
namespace UnityEngine {
  public struct Vector2Int : System.IEquatable<Vector2Int> {
    public int x, y; public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y); public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new(a.x - b.x, a.y - b.y);
    public static Vector2Int operator *(Vector2Int a, int d) => new(a.x * d, a.y * d);
    public static bool operator ==(Vector2Int a, Vector2Int b) => a.x == b.x && a.y == b.y; public static bool operator !=(Vector2Int a, Vector2Int b) => !(a == b);
    public static implicit operator Vector2(Vector2Int v) => new(v.x, v.y);
    public bool Equals(Vector2Int o) => this == o; public override bool Equals(object o) => o is Vector2Int v && this == v; public override int GetHashCode() => x * 73856093 ^ y;
  }
  public struct RectInt {
    public int x, y, width, height; public RectInt(int x, int y, int w, int h) { this.x = x; this.y = y; width = w; height = h; }
    public int xMin => x; public int yMin => y; public int xMax => x + width; public int yMax => y + height;
  }
  public struct Rect {
    public float x, y, width, height; public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
    public float xMin => x; public float yMin => y; public float xMax => x + width; public float yMax => y + height;
    public bool Overlaps(Rect o) => o.xMax > xMin && o.xMin < xMax && o.yMax > yMin && o.yMin < yMax;
  }
}
