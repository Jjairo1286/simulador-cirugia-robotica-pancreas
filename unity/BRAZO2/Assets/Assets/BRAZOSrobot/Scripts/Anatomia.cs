using System.Collections.Generic;
using UnityEngine;

namespace BrazosRobot
{
    /// <summary>
    /// Curva suave (Catmull-Rom) con sección elíptica: sirve para dibujar vasos, intestino y el propio
    /// páncreas, y también para medir la distancia de un punto a su superficie (contactos y lesiones).
    /// Semieje "a" a lo largo de <c>lat</c> (horizontal, perpendicular a la curva) y "b" a lo largo de <c>up</c>.
    /// Todo en milímetros, en el marco de la anatomía.
    /// </summary>
    public class Tubo
    {
        public string nombre;
        public Vector3[] p, tan, lat, up;
        public float[] a, b;
        public int n;

        public Tubo(string nombre, Vector3[] c, float r, int porTramo = 8)
            : this(nombre, c, Llenar(c.Length, r), null, porTramo) { }

        public Tubo(string nombre, Vector3[] c, float[] ra, float[] rb = null, int porTramo = 8)
        {
            this.nombre = nombre;
            if (rb == null) rb = ra;
            int m = c.Length;
            n = (m - 1) * porTramo + 1;
            p = new Vector3[n]; tan = new Vector3[n]; lat = new Vector3[n]; up = new Vector3[n];
            a = new float[n]; b = new float[n];
            for (int i = 0; i < m - 1; i++)
                for (int j = 0; j < porTramo; j++)
                {
                    float t = j / (float)porTramo; int k = i * porTramo + j;
                    p[k] = CR(c[Mathf.Max(i - 1, 0)], c[i], c[i + 1], c[Mathf.Min(i + 2, m - 1)], t);
                    float s = t * t * (3f - 2f * t);
                    a[k] = Mathf.Lerp(ra[i], ra[i + 1], s); b[k] = Mathf.Lerp(rb[i], rb[i + 1], s);
                }
            p[n - 1] = c[m - 1]; a[n - 1] = ra[m - 1]; b[n - 1] = rb[m - 1];
            for (int i = 0; i < n; i++)
            {
                Vector3 t = (p[Mathf.Min(i + 1, n - 1)] - p[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 r = Mathf.Abs(t.y) > 0.92f ? Vector3.right : Vector3.up;
                tan[i] = t;
                lat[i] = Vector3.Cross(r, t).normalized;
                up[i] = Vector3.Cross(t, lat[i]).normalized;
            }
        }

        static float[] Llenar(int m, float r) { var f = new float[m]; for (int i = 0; i < m; i++) f[i] = r; return f; }

        static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// Ruido suave y periódico alrededor de la sección (−1…1): da el aspecto lobulado.
        static float Ruido(float fi, float ang, float semilla)
        {
            float cx = Mathf.Cos(ang), sy = Mathf.Sin(ang);
            float n1 = Mathf.PerlinNoise(fi + semilla, cx * 1.3f + 7f) + Mathf.PerlinNoise(fi + semilla + 40f, sy * 1.3f + 7f);
            float n2 = Mathf.PerlinNoise(fi * 3.1f + semilla, cx * 4f + 3f) + Mathf.PerlinNoise(fi * 3.1f + semilla + 17f, sy * 4f + 3f);
            return (n1 - 1f) * 0.65f + (n2 - 1f) * 0.35f;
        }

        /// <param name="tapaIni">0 = abierto, 1 = extremo redondeado, 2 = corte plano</param>
        public Mesh Malla(int lados = 14, float ruido = 0f, float frec = 0.35f, int tapaIni = 1, int tapaFin = 1, float semilla = 0f)
        {
            var v = new List<Vector3>(); var ctr = new List<Vector3>(); var tri = new List<int>();
            int anillos = 0;
            float m0 = Mathf.Min(a[0], b[0]), m1 = Mathf.Min(a[n - 1], b[n - 1]);
            if (tapaIni == 1)
            {
                Anillo(v, ctr, p[0] - tan[0] * m0 * 0.62f, 0, 0.08f, lados, ruido, frec, semilla); anillos++;
                Anillo(v, ctr, p[0] - tan[0] * m0 * 0.48f, 0, 0.55f, lados, ruido, frec, semilla); anillos++;
                Anillo(v, ctr, p[0] - tan[0] * m0 * 0.24f, 0, 0.87f, lados, ruido, frec, semilla); anillos++;
            }
            else if (tapaIni == 2) { Anillo(v, ctr, p[0], 0, 0.01f, lados, 0f, frec, semilla); anillos++; }
            for (int i = 0; i < n; i++) { Anillo(v, ctr, p[i], i, 1f, lados, ruido, frec, semilla); anillos++; }
            if (tapaFin == 1)
            {
                Anillo(v, ctr, p[n - 1] + tan[n - 1] * m1 * 0.24f, n - 1, 0.87f, lados, ruido, frec, semilla); anillos++;
                Anillo(v, ctr, p[n - 1] + tan[n - 1] * m1 * 0.48f, n - 1, 0.55f, lados, ruido, frec, semilla); anillos++;
                Anillo(v, ctr, p[n - 1] + tan[n - 1] * m1 * 0.62f, n - 1, 0.08f, lados, ruido, frec, semilla); anillos++;
            }
            else if (tapaFin == 2) { Anillo(v, ctr, p[n - 1], n - 1, 0.01f, lados, 0f, frec, semilla); anillos++; }

            for (int r = 0; r < anillos - 1; r++)
                for (int k = 0; k < lados; k++)
                {
                    int k1 = (k + 1) % lados;
                    int A = r * lados + k, B = (r + 1) * lados + k, C = r * lados + k1, D = (r + 1) * lados + k1;
                    Mallas.Tri(v, ctr, tri, A, B, C);
                    Mallas.Tri(v, ctr, tri, C, B, D);
                }
            return Mallas.Crear(v, tri);
        }

        void Anillo(List<Vector3> v, List<Vector3> ctr, Vector3 c, int i, float esc, int lados, float ruido, float frec, float semilla)
        {
            for (int k = 0; k < lados; k++)
            {
                float ang = k * 2f * Mathf.PI / lados;
                float f = esc * (ruido > 0f ? 1f + ruido * Ruido(i * frec, ang, semilla) : 1f);
                v.Add(c + (lat[i] * (a[i] * Mathf.Cos(ang)) + up[i] * (b[i] * Mathf.Sin(ang))) * f);
                ctr.Add(c);
            }
        }

        public float Dist(Vector3 q) { int i; return Dist(q, out i); }

        /// Distancia aproximada de q a la superficie (negativa si está dentro) y tramo más cercano.
        public float Dist(Vector3 q, out int idx)
        {
            float best = float.MaxValue; idx = 0;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 ab = p[i + 1] - p[i]; float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(q - p[i], ab) / l2) : 0f;
                Vector3 d = q - (p[i] + ab * t);
                float ra = Mathf.Lerp(a[i], a[i + 1], t), rb = Mathf.Lerp(b[i], b[i + 1], t), rm = Mathf.Min(ra, rb);
                float x = Vector3.Dot(d, lat[i]), y = Vector3.Dot(d, up[i]), z = Vector3.Dot(d, tan[i]);
                float rho = Mathf.Sqrt(x * x / (ra * ra) + y * y / (rb * rb) + z * z / (rm * rm));
                float sd = rho > 1e-4f ? d.magnitude * (1f - 1f / rho) : -rm;
                if (sd < best) { best = sd; idx = i; }
            }
            return best;
        }

        /// Altura (y) de la cara anterior del tubo sobre el punto (x, z); −infinito si queda fuera.
        public float Techo(float x, float z)
        {
            float best = float.MaxValue; int k = 0;
            for (int i = 0; i < n; i++)
            {
                float dx = x - p[i].x, dz = z - p[i].z, d = dx * dx + dz * dz;
                if (d < best) { best = d; k = i; }
            }
            Vector3 q = new Vector3(x - p[k].x, 0f, z - p[k].z);
            float ax = Vector3.Dot(q, new Vector3(tan[k].x, 0f, tan[k].z).normalized);
            if ((k == 0 || k == n - 1) && Mathf.Abs(ax) > 1.5f) return float.NegativeInfinity;
            float lx = Vector3.Dot(q, lat[k]);
            float val = 1f - lx * lx / (a[k] * a[k]);
            return val <= 0f ? float.NegativeInfinity : p[k].y + b[k] * Mathf.Sqrt(val);
        }

        /// Punto de la curva en el índice fraccionario u (0 … n−1).
        public Vector3 En(float u)
        {
            u = Mathf.Clamp(u, 0f, n - 1f); int i = Mathf.Min((int)u, n - 2);
            return Vector3.Lerp(p[i], p[i + 1], u - i);
        }
    }

    /// <summary>Construcción de mallas en el marco de la anatomía.</summary>
    public static class Mallas
    {
        /// Añade el triángulo con la cara frontal hacia afuera (lejos de su centro) o hacia adentro.
        public static void Tri(List<Vector3> v, List<Vector3> ctr, List<int> tri, int a, int b, int c, bool adentro = false)
        {
            Vector3 f = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            Vector3 o = (v[a] - ctr[a]) + (v[b] - ctr[b]) + (v[c] - ctr[c]);
            bool ok = Vector3.Dot(f, o) >= 0f;
            if (adentro) ok = !ok;
            if (ok) { tri.Add(a); tri.Add(b); tri.Add(c); }
            else { tri.Add(a); tri.Add(c); tri.Add(b); }
        }

        public static Mesh Crear(List<Vector3> v, List<int> tri)
        {
            var m = new Mesh();
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// Elipsoide de semiejes r centrado en c. Con "adentro" se ve desde el interior (pared de la cavidad).
        public static Mesh Elipsoide(Vector3 c, Vector3 r, int segU = 28, int segV = 16, float ruido = 0f, bool adentro = false)
        {
            var v = new List<Vector3>(); var ctr = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i <= segV; i++)
            {
                float th = i * Mathf.PI / segV, st = Mathf.Sin(th), ct = Mathf.Cos(th);
                for (int j = 0; j < segU; j++)
                {
                    float ph = j * 2f * Mathf.PI / segU;
                    Vector3 d = new Vector3(st * Mathf.Cos(ph), ct, st * Mathf.Sin(ph));
                    float f = 1f;
                    if (ruido > 0f) f += ruido * (Mathf.PerlinNoise(d.x * 2.2f + 5f, d.y * 2.2f + 9f) + Mathf.PerlinNoise(d.z * 2.2f + 3f, d.x * 2.2f + 21f) - 1f);
                    v.Add(c + Vector3.Scale(d, r) * f); ctr.Add(c);
                }
            }
            for (int i = 0; i < segV; i++)
                for (int j = 0; j < segU; j++)
                {
                    int j1 = (j + 1) % segU;
                    int A = i * segU + j, B = (i + 1) * segU + j, C = i * segU + j1, D = (i + 1) * segU + j1;
                    if (i > 0) Tri(v, ctr, tri, A, B, C, adentro);
                    if (i < segV - 1) Tri(v, ctr, tri, C, B, D, adentro);
                }
            return Crear(v, tri);
        }
    }

    /// <summary>Tubo fino que se redibuja cada cuadro (hilo de sutura, cinta, aguja).</summary>
    public class TuboDin
    {
        public readonly GameObject go;
        readonly Mesh malla; readonly int lados; readonly float radio;
        Vector3[] v; int[] tri; int nPrev = -1;

        public TuboDin(Transform padre, string nombre, Material m, float radio, int lados = 6)
        {
            this.radio = radio; this.lados = lados;
            malla = new Mesh(); malla.MarkDynamic();
            go = Geo.Obj(malla, m, padre, nombre);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void Poner(Vector3[] p, int n)
        {
            if (n < 2) return;
            bool nuevo = n != nPrev;
            if (nuevo)
            {
                v = new Vector3[n * lados]; tri = new int[(n - 1) * lados * 6];
                int q = 0;
                for (int i = 0; i < n - 1; i++)
                    for (int k = 0; k < lados; k++)
                    {
                        int k1 = (k + 1) % lados;
                        int A = i * lados + k, B = (i + 1) * lados + k, C = i * lados + k1, D = (i + 1) * lados + k1;
                        tri[q++] = A; tri[q++] = C; tri[q++] = B;
                        tri[q++] = C; tri[q++] = D; tri[q++] = B;
                    }
                nPrev = n;
            }
            Vector3 t = Vector3.right;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = p[Mathf.Min(i + 1, n - 1)] - p[Mathf.Max(i - 1, 0)];
                if (d.sqrMagnitude > 1e-8f) t = d.normalized;
                Vector3 r = Mathf.Abs(t.y) > 0.9f ? Vector3.right : Vector3.up;
                Vector3 l = Vector3.Cross(r, t).normalized, u = Vector3.Cross(t, l);
                for (int k = 0; k < lados; k++)
                {
                    float ang = k * 2f * Mathf.PI / lados;
                    v[i * lados + k] = p[i] + (l * Mathf.Cos(ang) + u * Mathf.Sin(ang)) * radio;
                }
            }
            if (nuevo) { malla.Clear(); malla.vertices = v; malla.triangles = tri; }
            else malla.vertices = v;
            malla.RecalculateNormals(); malla.RecalculateBounds();
        }
    }

    public class Etiqueta
    {
        public string texto; public Vector3 p;
        public Etiqueta(string texto, Vector3 p) { this.texto = texto; this.p = p; }
    }

    /// <summary>Vaso u otra estructura que no debe lesionarse.</summary>
    public class Estructura
    {
        public string nombre; public Tubo tubo; public bool arteria; public float mlMin;
        public Estructura(string nombre, Tubo tubo, bool arteria, float mlMin) { this.nombre = nombre; this.tubo = tubo; this.arteria = arteria; this.mlMin = mlMin; }
    }

    /// <summary>
    /// Modelo anatómico del páncreas y sus relaciones, a escala real (milímetros).
    ///
    /// Marco: +X craneal (se aleja de la cámara), +Y anterior, +Z izquierda del paciente (derecha de la
    /// pantalla). El origen está en el cuello del páncreas, delante de la confluencia portal.
    ///
    /// Medidas de referencia: páncreas de unos 15 cm; cabeza ≈ 5 cm de alto y 2 cm de espesor, cuello
    /// &lt; 1 cm de espesor, cuerpo y cola 1–2 cm; vena mesentérica superior y porta ≈ 11–12 mm; vena
    /// esplénica ≈ 8 mm; arteria esplénica ≈ 5 mm (tortuosa, por el borde superior); arteria mesentérica
    /// superior ≈ 7 mm (a la izquierda y detrás de la vena); gastroduodenal ≈ 3,5 mm; colédoco ≈ 6 mm.
    /// </summary>
    public class Anatomia
    {
        public const float SUELO = -17f;                 // plano del retroperitoneo (grasa)
        public const float CUELLO_Y = 3.5f, CUELLO_A = 11f, CUELLO_B = 5.5f;
        public const float VMS_Y = -13.5f, VMS_R = 5.5f;

        public readonly Transform raiz;
        public readonly bool resecado;
        public Tubo pancreas, uncinado, ejePortal, vEsplenica, aEsplenica, ams, tCeliaco, aHepatica, agd, aGastricaIzq,
                    henle, vGastroepiploica, vColicaSup, vColicaMedia, vmi, coledoco, duodeno, yeyuno;
        public GameObject goPancreas;
        public Material mPancreas, mPancreasT;
        public readonly List<Etiqueta> etiquetas = new List<Etiqueta>();
        public readonly List<Estructura> vasos = new List<Estructura>();
        public Vector3 cBazo = new Vector3(40, -12, 126), rBazo = new Vector3(30, 18, 21);
        public Vector3 cHigado = new Vector3(92, 6, -64), rHigado = new Vector3(42, 22, 64);

        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        /// <param name="resecado">true = campo tras la pancreatoduodenectomía: sin cabeza ni duodeno, con el
        /// muñón pancreático (se ve el conducto de Wirsung) y el asa de yeyuno preparada para anastomosar.</param>
        public Anatomia(Transform raiz, bool resecado = false)
        {
            this.raiz = raiz; this.resecado = resecado;
            var mArt = Geo.Mat(0xc8322b, 0.05f, 0.6f);
            var mVena = Geo.Mat(0x3f5fb0, 0.05f, 0.6f);
            var mVenaP = Geo.Mat(0x5a54a8, 0.05f, 0.6f);
            var mBilis = Geo.Mat(0x5f9c4a, 0.05f, 0.5f);
            var mIntest = Geo.Mat(0xcf8674, 0f, 0.4f);
            mPancreas = Geo.Mat(0xd9984c, 0f, 0.3f);
            mPancreasT = Geo.Transparente(0xd9984c, 0.38f);

            // ---- lecho retroperitoneal y órganos vecinos ----
            Geo.Box(new Vector3(460, 14, 420), Geo.Mat(0xb98a45, 0f, 0.2f), raiz, V(-40, SUELO - 7f, 28)).name = "Retroperitoneo";
            Geo.Obj(Mallas.Elipsoide(cBazo, rBazo, 24, 14, 0.04f), Geo.Mat(0x7c3350, 0f, 0.55f), raiz, "Bazo");
            Geo.Obj(Mallas.Elipsoide(cHigado, rHigado, 28, 16, 0.03f), Geo.Mat(0x80392a, 0f, 0.6f), raiz, "Hígado");
            etiquetas.Add(new Etiqueta("Bazo", cBazo + V(-14, 16, -8)));
            etiquetas.Add(new Etiqueta("Hígado", cHigado + V(-34, 16, 20)));

            // ---- páncreas ----
            if (!resecado)
            {
                pancreas = new Tubo("Páncreas", new[] {
                    V(-14, -5, -54), V(-14, -4, -40), V(-7, -1, -20), V(0, CUELLO_Y, 0), V(4, 4, 18),
                    V(9, 3, 44), V(17, -1, 70), V(25, -6, 90), V(29, -9, 100) },
                    new[] { 10f, 25f, 18f, CUELLO_A, 14f, 15f, 12f, 8f, 4f },
                    new[] { 6f, 11f, 9f, CUELLO_B, 7f, 7.5f, 6.5f, 5f, 3f });
                goPancreas = Geo.Obj(pancreas.Malla(24, 0.11f, 0.6f, 1, 1, 3f), mPancreas, raiz, "Páncreas");
                uncinado = new Tubo("Proceso uncinado", new[] { V(-30, -10, -40), V(-32, -18, -22), V(-31, -22, -8), V(-30, -22.5f, 2) },
                    new[] { 8f, 8f, 7f, 4f }, new[] { 6f, 5f, 3.5f, 2.5f });
                Geo.Obj(uncinado.Malla(16, 0.1f, 0.6f, 1, 1, 9f), mPancreas, raiz, "Proceso uncinado");
                etiquetas.Add(new Etiqueta("Cabeza del páncreas", V(-14, 8, -40)));
                etiquetas.Add(new Etiqueta("Proceso uncinado", V(-33, -13, -24)));
            }
            else
            {
                pancreas = new Tubo("Páncreas (remanente)", new[] {
                    V(0, CUELLO_Y, 0), V(4, 4, 18), V(9, 3, 44), V(17, -1, 70), V(25, -6, 90), V(29, -9, 100) },
                    new[] { CUELLO_A, 14f, 15f, 12f, 8f, 4f },
                    new[] { CUELLO_B, 7f, 7.5f, 6.5f, 5f, 3f });
                goPancreas = Geo.Obj(pancreas.Malla(24, 0.11f, 0.6f, 2, 1, 3f), mPancreas, raiz, "Páncreas");
                // superficie de corte y conducto de Wirsung (≈ 3 mm)
                var corte = Geo.CylZ(1f, 0.5f, Geo.Mat(0xe8bd80, 0f, 0.3f), raiz, V(0, CUELLO_Y, -0.2f));
                corte.transform.localScale = new Vector3(2f * CUELLO_A * 0.95f, 0.25f, 2f * CUELLO_B * 0.95f);
                Geo.CylZ(1.9f, 0.6f, Geo.Mat(0xfaf3e6, 0f, 0.5f), raiz, V(1.5f, CUELLO_Y + 0.8f, -0.45f));
                Geo.CylZ(1.1f, 0.6f, Geo.Mat(0x40241c, 0f, 0.2f), raiz, V(1.5f, CUELLO_Y + 0.8f, -0.6f));
                etiquetas.Add(new Etiqueta("Conducto de Wirsung", V(1.5f, CUELLO_Y + 1f, -1f)));
                etiquetas.Add(new Etiqueta("Muñón pancreático", V(2, 10, 9)));
            }
            etiquetas.Add(new Etiqueta(resecado ? "Cuerpo" : "Cuello", resecado ? V(9, 11, 40) : V(0, 9.5f, 0)));
            if (!resecado) etiquetas.Add(new Etiqueta("Cuerpo", V(9, 11, 44)));
            etiquetas.Add(new Etiqueta("Cola", V(25, 0, 90)));

            // ---- eje venoso mesentérico-portal y afluentes ----
            ejePortal = new Tubo("Vena mesentérica superior / porta", new[] {
                V(-78, VMS_Y, 3), V(-45, VMS_Y, 1), V(-15, VMS_Y, 0), V(3, VMS_Y, 0), V(22, -14, -8), V(50, -15, -21) },
                new[] { 5.2f, VMS_R, VMS_R, VMS_R, 6f, 6.2f });
            Poner(ejePortal, mVenaP, 16);
            vasos.Add(new Estructura("vena mesentérica superior / porta", ejePortal, false, 180f));
            etiquetas.Add(new Etiqueta("V. mesentérica superior", V(-52, VMS_Y + 6, 1)));
            etiquetas.Add(new Etiqueta("Vena porta", V(34, -8, -13)));

            vEsplenica = new Tubo("Vena esplénica", new[] { V(4, -11.5f, 3), V(2, -7.5f, 18), V(7, -9, 44), V(15, -12, 70), V(23, -15.5f, 90), V(31, -17, 108) },
                new[] { 4.2f, 4f, 4f, 3.8f, 3.5f, 3.2f });
            Poner(vEsplenica, mVenaP, 12);
            vasos.Add(new Estructura("vena esplénica", vEsplenica, false, 120f));
            etiquetas.Add(new Etiqueta("V. esplénica", V(27, -13, 100)));

            vmi = new Tubo("Vena mesentérica inferior", new[] { V(-78, -15.5f, 26), V(-35, -15, 23), V(-8, -12, 21), V(1, -9, 19.5f) }, 2.2f);
            Poner(vmi, mVenaP, 10);
            vasos.Add(new Estructura("vena mesentérica inferior", vmi, false, 50f));
            etiquetas.Add(new Etiqueta("V. mesentérica inferior", V(-60, -12, 25)));

            vColicaMedia = new Tubo("Vena cólica media", new[] { V(-36, VMS_Y + 5.2f, 1), V(-48, -4, 3), V(-58, -5.5f, 5), V(-78, -3, 9) }, 1.6f);
            Poner(vColicaMedia, mVena, 8);
            vasos.Add(new Estructura("vena cólica media", vColicaMedia, false, 60f));
            etiquetas.Add(new Etiqueta("V. cólica media", V(-70, -1, 8)));

            if (!resecado)
            {
                // tronco gastrocólico de Henle: gastroepiploica derecha + cólica superior derecha (+ pancreatoduodenal)
                henle = new Tubo("Tronco gastrocólico de Henle", new[] { V(-22, VMS_Y + 3.2f, -4.5f), V(-24, -6, -12), V(-27, -3, -21) }, 2f);
                Poner(henle, mVena, 10);
                vasos.Add(new Estructura("tronco gastrocólico de Henle", henle, false, 80f));
                vGastroepiploica = new Tubo("Vena gastroepiploica derecha", new[] { V(-27, -3, -21), V(-25, 6.8f, -30), V(-10, 9.6f, -36), V(12, 9, -33) }, 1.4f);
                Poner(vGastroepiploica, mVena, 8);
                vasos.Add(new Estructura("vena gastroepiploica derecha", vGastroepiploica, false, 40f));
                vColicaSup = new Tubo("Vena cólica superior derecha", new[] { V(-27, -3, -21), V(-41, -5, -28), V(-62, -9, -36), V(-78, -11, -40) }, 1.5f);
                Poner(vColicaSup, mVena, 8);
                vasos.Add(new Estructura("vena cólica superior derecha", vColicaSup, false, 40f));
                etiquetas.Add(new Etiqueta("Tronco de Henle", V(-25, -3, -15)));
                etiquetas.Add(new Etiqueta("V. gastroepiploica der.", V(-6, 11.5f, -36)));
            }

            // ---- arterias ----
            ams = new Tubo("Arteria mesentérica superior", new[] { V(12, -27, 10), V(0, -19.5f, 11), V(-22, -18, 12), V(-50, -17, 13), V(-78, -17, 14) }, 3.5f);
            Poner(ams, mArt, 12);
            vasos.Add(new Estructura("arteria mesentérica superior", ams, true, 250f));
            etiquetas.Add(new Etiqueta("A. mesentérica superior", V(-42, -13, 13.5f)));

            tCeliaco = new Tubo("Tronco celíaco", new[] { V(20, -27, 9), V(20, -19, 8.5f), V(20, -13, 8) }, 3.3f, 5);
            Poner(tCeliaco, mArt, 12);
            vasos.Add(new Estructura("tronco celíaco", tCeliaco, true, 300f));
            etiquetas.Add(new Etiqueta("Tronco celíaco", V(21, -9, 8)));

            aEsplenica = new Tubo("Arteria esplénica", new[] {
                V(20, -13, 8), V(21, -4, 18), V(27, 0, 30), V(22, -1, 42), V(28, 0, 54), V(27, -3, 66), V(33, -4, 78), V(31, -8, 90), V(36, -12, 106) }, 2.6f);
            Poner(aEsplenica, mArt, 10);
            vasos.Add(new Estructura("arteria esplénica", aEsplenica, true, 200f));
            etiquetas.Add(new Etiqueta("A. esplénica", V(28, 3, 54)));

            aHepatica = new Tubo("Arteria hepática", new[] { V(20, -13, 8), V(21, -10, -6), V(20, -7, -22), V(24, -6, -30), V(42, -8, -33), V(62, -10, -35) },
                new[] { 2.6f, 2.5f, 2.5f, 2.3f, 2.2f, 2.2f });
            Poner(aHepatica, mArt, 10);
            vasos.Add(new Estructura("arteria hepática", aHepatica, true, 200f));
            etiquetas.Add(new Etiqueta("A. hepática común", V(21, -5, -12)));

            aGastricaIzq = new Tubo("Arteria gástrica izquierda", new[] { V(20, -13, 8), V(33, -5, 11), V(50, 5, 10) }, 1.7f);
            Poner(aGastricaIzq, mArt, 8);
            vasos.Add(new Estructura("arteria gástrica izquierda", aGastricaIzq, true, 120f));

            coledoco = new Tubo("Colédoco", resecado
                ? new[] { V(64, -6, -42), V(48, -7, -40), V(36, -7.5f, -39) }
                : new[] { V(64, -6, -42), V(40, -7, -39), V(22, -8, -41), V(4, -10, -45), V(-14, -10, -53), V(-20, -9, -58) }, 3f);
            Poner(coledoco, mBilis, 10);
            etiquetas.Add(new Etiqueta("Colédoco", V(46, -3, -40)));

            if (!resecado)
            {
                agd = new Tubo("Arteria gastroduodenal", new[] { V(22, -6.3f, -27), V(12, 0, -25), V(2, 9.2f, -24), V(-12, 9.4f, -24), V(-22, 7.6f, -25) }, 1.8f);
                Poner(agd, mArt, 8);
                vasos.Add(new Estructura("arteria gastroduodenal", agd, true, 150f));
                etiquetas.Add(new Etiqueta("A. gastroduodenal", V(-4, 11.5f, -24)));

                duodeno = new Tubo("Duodeno", new[] {
                    V(24, 6, -16), V(26, 2, -40), V(20, -3, -60), V(0, -6, -67), V(-25, -7, -67), V(-46, -9, -61),
                    V(-56, -15, -40), V(-59, -26, -18), V(-59, -31, 5), V(-55, -33, 26), V(-48, -34, 40), V(-40, -35, 52) }, 12.5f, 6);
                Poner(duodeno, mIntest, 18, 0.03f);
                etiquetas.Add(new Etiqueta("Duodeno (2.ª porción)", V(-12, 6, -68)));
                etiquetas.Add(new Etiqueta("Píloro", V(24, 19, -14)));
            }
            else
            {
                // muñón de la gastroduodenal ligada y asa de yeyuno subida para la pancreatoyeyunostomía
                agd = new Tubo("Muñón de la a. gastroduodenal", new[] { V(22, -6.3f, -27), V(17, -3, -26), V(13, -1, -25.5f) }, 1.8f);
                Poner(agd, mArt, 8);
                vasos.Add(new Estructura("muñón de la arteria gastroduodenal", agd, true, 150f));
                yeyuno = new Tubo("Asa de yeyuno", new[] { V(-78, -6, -34), V(-45, -1, -18), V(-16, 2f, -9), V(6, 2f, -9), V(17, 2f, -9.5f) },
                    new[] { 11f, 11f, 11f, 11f, 10f }, new[] { 8f, 7.5f, 7f, 7f, 6.5f });
                Poner(yeyuno, mIntest, 18, 0.025f);
                etiquetas.Add(new Etiqueta("Asa de yeyuno", V(-30, 9, -16)));
            }
        }

        GameObject Poner(Tubo t, Material m, int lados, float ruido = 0f)
        {
            return Geo.Obj(t.Malla(lados, ruido, 0.3f, 1, 1, lados), m, raiz, t.nombre);
        }

        static bool EnElipsoide(Vector3 q, Vector3 c, Vector3 r, float margen)
        {
            Vector3 d = q - c;
            return d.x * d.x / (r.x * r.x) + d.y * d.y / (r.y * r.y) + d.z * d.z / (r.z * r.z) < margen * margen;
        }
        public bool EnBazo(Vector3 q) { return EnElipsoide(q, cBazo, rBazo, 1.04f); }
        public bool EnHigado(Vector3 q) { return EnElipsoide(q, cHigado, rHigado, 1.04f); }

        /// Vaso más cercano al punto q y distancia a su pared (negativa = dentro).
        public Estructura VasoCercano(Vector3 q, out float dist)
        {
            Estructura mejor = null; dist = float.MaxValue;
            foreach (var e in vasos)
            {
                float d = e.tubo.Dist(q);
                if (d < dist) { dist = d; mejor = e; }
            }
            return mejor;
        }

        /// Superficie sobre la que descansa un hilo o una aguja en (x, z).
        public float Suelo(float x, float z)
        {
            float y = SUELO;
            y = Mathf.Max(y, ejePortal.Techo(x, z));
            y = Mathf.Max(y, ams.Techo(x, z));
            if (yeyuno != null) y = Mathf.Max(y, yeyuno.Techo(x, z));
            return y;
        }

        /// Hace translúcido el páncreas (ayuda visual al trabajar por detrás del cuello).
        public void Translucido(bool on)
        {
            var r = goPancreas.GetComponent<MeshRenderer>();
            var m = on ? mPancreasT : mPancreas;
            if (r.sharedMaterial != m) r.sharedMaterial = m;
        }
    }
}
