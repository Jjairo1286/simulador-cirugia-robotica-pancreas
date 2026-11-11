using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BrazosRobot
{
    /// <summary>
    /// Utilidades para construir la escena por código: materiales (Built-in o URP),
    /// primitivas sin collider, tubos con esquina redondeada, toros y mordazas cónicas.
    /// Todas las medidas que reciben están en el espacio local del padre (milímetros).
    /// </summary>
    public static class Geo
    {
        static Mesh cyl, cube, sphere;
        static Shader lit;
        static bool urp;

        static void Init()
        {
            if (cyl != null) return;
            cyl = PrimMesh(PrimitiveType.Cylinder);
            cube = PrimMesh(PrimitiveType.Cube);
            sphere = PrimMesh(PrimitiveType.Sphere);
            urp = GraphicsSettings.currentRenderPipeline != null;
            lit = urp ? Shader.Find("Universal Render Pipeline/Lit") : null;
            if (lit == null) { lit = Shader.Find("Standard"); urp = false; }
        }

        static Mesh PrimMesh(PrimitiveType t)
        {
            var g = GameObject.CreatePrimitive(t);
            var m = g.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(g);   // inmediato: que su collider no llegue a la física
            return m;
        }

        // ---------- materiales ----------
        public static Color Hex(int h, float a = 1f)
        {
            return new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f, a);
        }

        public static Material Mat(int hex, float metal = 0.1f, float smooth = 0.5f)
        {
            return Mat(Hex(hex), metal, smooth);
        }

        public static Material Mat(Color c, float metal = 0.1f, float smooth = 0.5f)
        {
            Init();
            var m = new Material(lit);
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }

        public static Material Transparente(int hex, float alpha)
        {
            var m = Mat(Hex(hex, alpha), 0f, 0.2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            if (urp)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            else
            {
                m.SetFloat("_Mode", 3f);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            m.renderQueue = 3000;
            return m;
        }

        // ---------- objetos ----------
        public static Transform Grupo(Transform parent, string name, Vector3 pos)
        {
            var g = new GameObject(name).transform;
            g.SetParent(parent, false);
            g.localPosition = pos;
            return g;
        }

        public static GameObject Obj(Mesh mesh, Material mat, Transform parent, string name = "pieza")
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            g.layer = parent.gameObject.layer;
            return g;
        }

        /// Cilindro a lo largo de Y local (r = radio, l = largo)
        public static GameObject CylY(float r, float l, Material m, Transform p, Vector3 pos)
        {
            Init();
            var g = Obj(cyl, m, p, "cilindro");
            g.transform.localPosition = pos;
            g.transform.localScale = new Vector3(2 * r, l / 2f, 2 * r);
            return g;
        }

        /// Cilindro a lo largo de X local
        public static GameObject CylX(float r, float l, Material m, Transform p, Vector3 pos)
        {
            var g = CylY(r, l, m, p, pos);
            g.transform.localRotation = Quaternion.AngleAxis(90f, Vector3.forward);
            return g;
        }

        /// Cilindro a lo largo de Z local
        public static GameObject CylZ(float r, float l, Material m, Transform p, Vector3 pos)
        {
            var g = CylY(r, l, m, p, pos);
            g.transform.localRotation = Quaternion.AngleAxis(90f, Vector3.right);
            return g;
        }

        public static GameObject Box(Vector3 size, Material m, Transform p, Vector3 pos)
        {
            Init();
            var g = Obj(cube, m, p, "caja");
            g.transform.localPosition = pos;
            g.transform.localScale = size;
            return g;
        }

        public static GameObject Sphere(float r, Material m, Transform p, Vector3 pos)
        {
            Init();
            var g = Obj(sphere, m, p, "esfera");
            g.transform.localPosition = pos;
            g.transform.localScale = Vector3.one * 2f * r;
            return g;
        }

        /// Cilindro de radio r que va del punto a al punto b (en el espacio del padre)
        public static GameObject Link(float r, Material m, Transform p)
        {
            return CylY(r, 1f, m, p, Vector3.zero);
        }

        public static void Place(Transform t, Vector3 a, Vector3 b, float r)
        {
            Vector3 d = b - a;
            float L = Mathf.Max(d.magnitude, 0.001f);
            t.localPosition = (a + b) * 0.5f;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, d / L);
            t.localScale = new Vector3(2 * r, L / 2f, 2 * r);
        }

        // ---------- ele con la esquina redondeada ----------
        /// Tramo A→esquina C, arco de 90° de radio rf, tramo C→B. Primera mitad con matA y segunda con matB.
        public static void LRod(Transform parent, Material matA, Material matB, Vector3 A, Vector3 C, Vector3 B, float rf, float rad = 6f)
        {
            Vector3 u1 = (A - C).normalized, u2 = (B - C).normalized;
            Vector3 P1 = C + u1 * rf, O = P1 + u2 * rf;
            Vector3 n = Vector3.Cross(u1, u2).normalized;
            var p1 = new List<Vector3> { A }; var t1 = new List<Vector3> { -u1 };
            var p2 = new List<Vector3>(); var t2 = new List<Vector3>();
            const int N = 16;
            for (int i = 0; i <= N; i++)
            {
                float q = i / (float)N * Mathf.PI / 2f;
                Vector3 p = O - rf * (u2 * Mathf.Cos(q) + u1 * Mathf.Sin(q));
                Vector3 t = (u2 * Mathf.Sin(q) - u1 * Mathf.Cos(q)).normalized;
                if (i <= N / 2) { p1.Add(p); t1.Add(t); }
                if (i >= N / 2) { p2.Add(p); t2.Add(t); }
            }
            p2.Add(B); t2.Add(u2);
            Obj(Tube(p1, t1, n, rad), matA, parent, "ele");
            Obj(Tube(p2, t2, n, rad), matB, parent, "ele");
            Sphere(rad, matA, parent, A);
            Sphere(rad, matB, parent, B);
        }

        static Mesh Tube(List<Vector3> pts, List<Vector3> tan, Vector3 n, float rad)
        {
            const int S = 18;
            var v = new List<Vector3>(); var nr = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 t = tan[i];
                Vector3 b = Vector3.Cross(t, n).normalized;
                Vector3 nn = Vector3.Cross(b, t).normalized;
                for (int k = 0; k <= S; k++)
                {
                    float a = k * 2f * Mathf.PI / S;
                    Vector3 d = nn * Mathf.Cos(a) + b * Mathf.Sin(a);
                    v.Add(pts[i] + d * rad); nr.Add(d);
                }
            }
            for (int i = 0; i < pts.Count - 1; i++)
                for (int k = 0; k < S; k++)
                {
                    int a = i * (S + 1) + k, b = a + S + 1, c = a + 1, d = b + 1;
                    Tri(v, nr, tri, a, b, c);
                    Tri(v, nr, tri, c, b, d);
                }
            return Build(v, nr, tri);
        }

        /// Toro en el plano XZ (eje Y). R = radio mayor, r = radio del tubo.
        public static Mesh Torus(float R, float r, int segU = 32, int segV = 12)
        {
            var v = new List<Vector3>(); var nr = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i <= segU; i++)
            {
                float u = i * 2f * Mathf.PI / segU;
                Vector3 c = new Vector3(Mathf.Cos(u), 0, Mathf.Sin(u));
                for (int k = 0; k <= segV; k++)
                {
                    float w = k * 2f * Mathf.PI / segV;
                    Vector3 d = c * Mathf.Cos(w) + Vector3.up * Mathf.Sin(w);
                    v.Add(c * R + d * r); nr.Add(d);
                }
            }
            for (int i = 0; i < segU; i++)
                for (int k = 0; k < segV; k++)
                {
                    int a = i * (segV + 1) + k, b = a + segV + 1, c = a + 1, d = b + 1;
                    Tri(v, nr, tri, a, b, c);
                    Tri(v, nr, tri, c, b, d);
                }
            return Build(v, nr, tri);
        }

        /// Cilindro con extremos redondeados de a a b (en el espacio del padre). Devuelve el objeto que lo agrupa.
        public static GameObject Capsula(Transform p, Vector3 a, Vector3 b, float r, Material m)
        {
            var g = new GameObject("capsula"); g.transform.SetParent(p, false); g.layer = p.gameObject.layer;
            var c = Link(r, m, g.transform); Place(c.transform, a, b, r);
            Sphere(r, m, g.transform, a); Sphere(r, m, g.transform, b);
            return g;
        }

        /// Mordaza con forma de instrumento comercial: sección en "D" (cara interna plana en z = 0, que es
        /// donde se cierran las dos mordazas), alto y ancho que se afinan hacia la punta, curvatura hacia
        /// arriba (Maryland, tijera curva) y punta redondeada o aguda. Largo L a lo largo de +X.
        public static Mesh Mandibula(float L, float hB, float hT, float wB, float wT, float curva, float aguda, float side)
        {
            const int NX = 22, NF = 12;
            var v = new List<Vector3>(); var ctr = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i <= NX; i++)
            {
                float s = i / (float)NX, x = L * s;
                float h = Mathf.Lerp(hB, hT, s), w = Mathf.Lerp(wB, wT, s);
                float f = 1f;
                if (s > 0.8f)
                {
                    float u = (s - 0.8f) / 0.2f;
                    float redondo = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)), punta = 1f - u;
                    f = Mathf.Max(0.04f, Mathf.Lerp(redondo, punta, aguda));
                }
                float cy = curva * s * s;
                Vector3 c = new Vector3(x, cy, side * w * f * 0.42f);
                for (int k = 0; k <= NF; k++)
                {
                    float phi = -Mathf.PI / 2f + Mathf.PI * k / NF;
                    v.Add(new Vector3(x, cy + h * 0.5f * f * Mathf.Sin(phi), side * w * f * Mathf.Cos(phi)));
                    ctr.Add(c);
                }
            }
            int R = NF + 1;
            for (int i = 0; i < NX; i++)
                for (int k = 0; k < R; k++)
                {
                    int k1 = (k + 1) % R;
                    int A = i * R + k, B = (i + 1) * R + k, C = i * R + k1, D = (i + 1) * R + k1;
                    Mallas.Tri(v, ctr, tri, A, B, C);
                    Mallas.Tri(v, ctr, tri, C, B, D);
                }
            // tapa de la base
            int c0 = v.Count; v.Add(new Vector3(0, 0, side * wB * 0.42f)); ctr.Add(new Vector3(1, 0, side * wB * 0.42f));
            for (int k = 0; k < R; k++) Mallas.Tri(v, ctr, tri, c0, k, (k + 1) % R);
            return Mallas.Crear(v, tri);
        }

        /// Mordaza: caja de largo L en +X que se afina hacia la punta (como en el visor HTML).
        public static Mesh Mordaza(float L, float h, float w, float side)
        {
            // esquinas: base en x=0, punta en x=L (alto ×0.4, ancho ×0.55 hacia la línea media)
            Vector3[] c = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                bool tipEnd = (i & 1) != 0, top = (i & 2) != 0, outer = (i & 4) != 0;
                float x = tipEnd ? L : 0f;
                float y = (top ? 1 : -1) * h / 2f * (tipEnd ? 0.4f : 1f);
                float z = side * (outer ? w : 0f) * (tipEnd ? 0.55f : 1f);
                c[i] = new Vector3(x, y, z);
            }
            int[][] caras = {
                new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 },   // base, punta
                new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 },   // abajo, arriba
                new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 } }; // interior, exterior
            Vector3 centro = Vector3.zero; foreach (var p in c) centro += p; centro /= 8f;
            var v = new List<Vector3>(); var nr = new List<Vector3>(); var tri = new List<int>();
            foreach (var f in caras)
            {
                Vector3 fc = (c[f[0]] + c[f[1]] + c[f[2]] + c[f[3]]) / 4f;
                Vector3 nrm = Vector3.Cross(c[f[1]] - c[f[0]], c[f[2]] - c[f[0]]).normalized;
                if (Vector3.Dot(nrm, fc - centro) < 0) nrm = -nrm;
                int b0 = v.Count;
                for (int k = 0; k < 4; k++) { v.Add(c[f[k]]); nr.Add(nrm); }
                Tri(v, nr, tri, b0, b0 + 1, b0 + 2);
                Tri(v, nr, tri, b0, b0 + 2, b0 + 3);
            }
            return Build(v, nr, tri);
        }

        // Añade el triángulo con el orden que deja su cara frontal hacia afuera (convención de Unity)
        static void Tri(List<Vector3> v, List<Vector3> nr, List<int> tri, int a, int b, int c)
        {
            Vector3 f = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            Vector3 o = nr[a] + nr[b] + nr[c];
            if (Vector3.Dot(f, o) >= 0) { tri.Add(a); tri.Add(b); tri.Add(c); }
            else { tri.Add(a); tri.Add(c); tri.Add(b); }
        }

        static Mesh Build(List<Vector3> v, List<Vector3> nr, List<int> tri)
        {
            var m = new Mesh();
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(nr); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        public static void SetLayer(GameObject g, int layer)
        {
            g.layer = layer;
            foreach (Transform t in g.transform) SetLayer(t.gameObject, layer);
        }
    }
}
