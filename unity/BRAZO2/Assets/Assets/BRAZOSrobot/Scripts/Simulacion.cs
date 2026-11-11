using System.Collections.Generic;
using UnityEngine;

namespace BrazosRobot
{
    /// <summary>Estado de un instrumento en el marco de la anatomía (milímetros).</summary>
    public class Herr
    {
        public int mano;                    // 0 = derecha, 1 = izquierda
        public Vector3 pos;                 // centro de las mordazas (donde agarra)
        public Vector3 punta;               // extremo de las mordazas
        public Vector3 dir, eje;            // dirección de las mordazas y eje de su bisagra
        public Vector3 muneca, trocar;      // centro de la muñeca y punto de entrada (RCM)
        public Vector3 previa;              // pos del cuadro anterior
        public float ap, rapidez;           // apertura (grados) y rapidez filtrada (mm/s)
        public bool cerrada, cerro, abrio;  // estado de la pinza y flancos de este cuadro
        public Vector3 Lado { get { return Vector3.Cross(dir, eje); } }
    }

    public static class Mat3
    {
        public static float PuntoSeg(Vector3 q, Vector3 a, Vector3 b, out float t)
        {
            Vector3 ab = b - a; float l2 = ab.sqrMagnitude;
            t = l2 > 1e-9f ? Mathf.Clamp01(Vector3.Dot(q - a, ab) / l2) : 0f;
            return (q - (a + ab * t)).magnitude;
        }

        /// Distancia mínima entre los segmentos p1–q1 y p2–q2.
        public static float SegSeg(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            const float EPS = 1e-9f;
            if (a <= EPS && e <= EPS) return r.magnitude;
            if (a <= EPS) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= EPS) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den > EPS ? Mathf.Clamp01((b * f - c * e) / den) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return ((p1 + d1 * s) - (p2 + d2 * t)).magnitude;
        }

        public static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    public static class Util
    {
        public static void Destruir(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }

    /// <summary>Elementos del panel de información (IMGUI).</summary>
    public static class Hud
    {
        public static GUIStyle titulo, texto, nota, aviso, grande, etiqueta, dato;
        static Texture2D blanco;

        public static void Estilos()
        {
            if (titulo != null) return;
            blanco = Texture2D.whiteTexture;
            titulo = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 15 };
            texto = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13 };
            nota = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
            aviso = new GUIStyle(nota); aviso.normal.textColor = new Color(1f, 0.55f, 0.45f);
            grande = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 20, wordWrap = true };
            dato = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            etiqueta = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
            etiqueta.normal.textColor = Color.white;
        }

        /// Barra 0…1 con una zona verde opcional [zMin, zMax].
        public static void Barra(float valor, float zMin = 0f, float zMax = 0f)
        {
            Rect r = GUILayoutUtility.GetRect(100f, 14f, GUILayout.ExpandWidth(true));
            Color previo = GUI.color;
            GUI.color = new Color(0.13f, 0.13f, 0.13f); GUI.DrawTexture(r, blanco);
            if (zMax > zMin)
            {
                GUI.color = new Color(0.1f, 0.5f, 0.15f);
                GUI.DrawTexture(new Rect(r.x + r.width * zMin, r.y, r.width * (zMax - zMin), r.height), blanco);
            }
            float v = Mathf.Clamp01(valor);
            bool hayZona = zMax > zMin;
            if (!hayZona)
            {
                GUI.color = new Color(0.35f, 0.75f, 1f);
                GUI.DrawTexture(new Rect(r.x, r.y + 3f, r.width * v, r.height - 6f), blanco);
            }
            else
            {
                GUI.color = (v >= zMin && v <= zMax) ? new Color(0.45f, 1f, 0.45f) : (v > zMax ? new Color(1f, 0.3f, 0.3f) : Color.white);
                GUI.DrawTexture(new Rect(r.x + r.width * v - 2f, r.y - 2f, 4f, r.height + 4f), blanco);
            }
            GUI.color = previo;
            GUILayout.Space(3f);
        }

        public static void Dato(string nombre, string valor)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(nombre, dato, GUILayout.Width(190f));
            GUILayout.Label(valor, dato);
            GUILayout.EndHorizontal();
        }

        public static void Tinte(string t, Color c, GUIStyle s)
        {
            Color previo = GUI.color; GUI.color = c; GUILayout.Label(t, s); GUI.color = previo;
        }
    }

    /// <summary>
    /// Tejido blando simplificado sobre una malla: al tomarlo, los vértices cercanos siguen a la pinza con
    /// caída gaussiana. Tensión = desplazamiento / maxEstir (0…1); al llegar a 1 se desgarra.
    /// Como el mando no tiene háptica, la tensión se comunica con el color (palidez = isquemia) y la barra.
    /// </summary>
    public class Tejido
    {
        public string nombre;
        public float maxEstir, radioInfl, radioAncla;
        public Vector3 ancla;
        public float inicioPalidez = 0.75f;
        public bool agarrado; public float tension; public Vector3 desp; public int lesiones;
        public Vector3 pAgarre;

        readonly Mesh malla; readonly Vector3[] reposo, act; readonly float[] w;
        readonly Material mat; readonly Color col0;
        Vector3 velRet, despApl; float destello; bool pesosListos;
        static readonly Color ISQUEMIA = new Color(0.97f, 0.93f, 0.90f), LESION = new Color(0.42f, 0.06f, 0.10f);

        public Tejido(string nombre, Mesh malla, Material mat, float maxEstir, float radioInfl)
        {
            this.nombre = nombre; this.malla = malla; this.mat = mat; this.maxEstir = maxEstir; this.radioInfl = radioInfl;
            malla.MarkDynamic();
            reposo = malla.vertices; act = (Vector3[])reposo.Clone(); w = new float[reposo.Length];
            col0 = mat.color;
        }

        float FAncla(Vector3 p)
        {
            if (radioAncla <= 0f) return 1f;
            return Mathf.SmoothStep(0f, 1f, (p - ancla).magnitude / radioAncla);
        }

        float Peso(Vector3 p)
        {
            float d = (p - pAgarre).magnitude;
            return Mathf.Clamp01(Mathf.Exp(-(d * d) / (radioInfl * radioInfl)) * FAncla(p) / Mathf.Max(FAncla(pAgarre), 0.2f));
        }

        /// Empieza a arrastrar el tejido desde el punto p (en reposo).
        public void Iniciar(Vector3 p)
        {
            pAgarre = p; desp = Vector3.zero; velRet = Vector3.zero;
            for (int i = 0; i < reposo.Length; i++) w[i] = Peso(reposo[i]);
            pesosListos = true; agarrado = true;
        }

        /// Posición del punto de agarre según la pinza. Devuelve true si el tejido se desgarró.
        public bool Arrastrar(Vector3 pPinza)
        {
            if (!agarrado) return false;
            desp = pPinza - pAgarre;
            tension = desp.magnitude / maxEstir;
            if (tension >= 1f) { lesiones++; destello = 1f; agarrado = false; return true; }
            return false;
        }

        /// Desplazamiento impuesto desde fuera (p. ej. por la cinta que suspende el órgano).
        public bool Imponer(Vector3 p, Vector3 d)
        {
            if (!pesosListos || (p - pAgarre).sqrMagnitude > 0.01f) Iniciar(p);
            agarrado = d.sqrMagnitude > 1e-6f;
            desp = d; tension = d.magnitude / maxEstir;
            if (tension >= 1f) { lesiones++; destello = 1f; return true; }
            return false;
        }

        public void Soltar() { agarrado = false; }
        public void Lesion() { lesiones++; destello = 1f; }

        /// Dónde queda ahora un punto que en reposo está en p (para lo que va unido al tejido).
        public Vector3 Mover(Vector3 p) { return pesosListos ? p + desp * Peso(p) : p; }

        public void Paso(float dt)
        {
            if (!agarrado && desp != Vector3.zero)
            {
                desp = Vector3.SmoothDamp(desp, Vector3.zero, ref velRet, 0.15f, Mathf.Infinity, dt);
                if (desp.sqrMagnitude < 1e-4f) desp = Vector3.zero;
                tension = desp.magnitude / maxEstir;
            }
            if (desp != despApl && pesosListos)
            {
                for (int i = 0; i < reposo.Length; i++) act[i] = reposo[i] + desp * w[i];
                malla.vertices = act; malla.RecalculateNormals(); malla.RecalculateBounds();
                despApl = desp;
            }
            Color c = Color.Lerp(col0, ISQUEMIA, Mathf.InverseLerp(inicioPalidez, 1f, tension));
            c = Color.Lerp(c, LESION, Mathf.Clamp01(lesiones * 0.15f));
            c = Color.Lerp(c, Color.red, destello);
            c.a = col0.a;
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            destello = Mathf.MoveTowards(destello, 0f, dt * 1.5f);
        }
    }

    /// <summary>
    /// Hilo o cinta: cadena de puntos unidos por tramos que no se estiran (pueden quedar flojos).
    /// Las pinzas lo sujetan en un punto; opcionalmente pasa por "ojales" fijos (el túnel por detrás del
    /// páncreas, los orificios de la aguja) por los que se desliza cuando se tira de un lado.
    /// </summary>
    public class Hilo
    {
        public readonly int n; public readonly float seg, largo;
        public readonly Vector3[] p;
        public System.Func<float, float, float> suelo;
        public float radio = 0.5f;
        public readonly List<Vector3> ojal = new List<Vector3>();
        public float u;                 // longitud de hilo entre el extremo 0 y el primer ojal
        public float exceso;            // mm que le faltan al hilo (tensión) con los dos lados sujetos
        public float holgura;           // mm que sobran en total con los dos lados sujetos
        public bool sujetoA, sujetoB;   // hay una pinza a cada lado de los ojales
        public Vector3 posA, posB;

        struct Pin { public float s; public Vector3 pos; }
        readonly List<Pin> pins = new List<Pin>(), todos = new List<Pin>();
        float D;

        public Hilo(int n, float seg)
        {
            this.n = n; this.seg = seg; largo = (n - 1) * seg; p = new Vector3[n];
        }

        public float LibreA { get { return u; } }
        public float LibreB { get { return largo - (u + D); } }
        public float TramoOjales { get { return D; } }

        public void SinAgarres() { pins.Clear(); }
        public void Agarrar(int k, Vector3 pos) { pins.Add(new Pin { s = Mathf.Clamp(k, 0, n - 1) * seg, pos = pos }); }

        public void PonerOjales(IList<Vector3> pts, float u0)
        {
            ojal.Clear(); ojal.AddRange(pts);
            D = 0f; for (int i = 1; i < ojal.Count; i++) D += (ojal[i] - ojal[i - 1]).magnitude;
            u = Mathf.Clamp(u0, 0f, largo - D);
        }
        public void QuitarOjales() { ojal.Clear(); D = 0f; }

        public int Cercano(Vector3 q, out float d)
        {
            int k = 0; float best = float.MaxValue;
            for (int i = 0; i < n; i++) { float x = (p[i] - q).sqrMagnitude; if (x < best) { best = x; k = i; } }
            d = Mathf.Sqrt(best); return k;
        }

        /// ¿El punto k está metido en el tejido (entre los ojales)?
        public bool EnOjales(int k) { return ojal.Count > 0 && k * seg > u - 0.01f && k * seg < u + D + 0.01f; }

        static void Limitar(ref Vector3 q, Vector3 ancla, float max)
        {
            Vector3 d = q - ancla; float m = d.magnitude;
            if (m > max) q = ancla + d * (max / Mathf.Max(m, 1e-6f));
        }

        public void Paso(float dt)
        {
            // 1) gravedad sencilla (medio viscoso) sobre lo que no está sujeto
            if (suelo != null)
                for (int k = 0; k < n; k++)
                {
                    if (EnOjales(k)) continue;
                    float f = suelo(p[k].x, p[k].z) + radio;
                    p[k].y = Mathf.Max(f, p[k].y - 70f * dt);
                }

            // 2) deslizamiento por los ojales
            exceso = 0f; holgura = 0f; sujetoA = sujetoB = false;
            if (ojal.Count > 0)
            {
                D = 0f; for (int i = 1; i < ojal.Count; i++) D += (ojal[i] - ojal[i - 1]).magnitude;
                u = Mathf.Clamp(u, 0f, largo - D);
                float sA = -1f, sB = float.MaxValue;
                foreach (var pin in pins)
                {
                    if (pin.s <= u && pin.s > sA) { sA = pin.s; posA = pin.pos; sujetoA = true; }
                    if (pin.s >= u + D && pin.s < sB) { sB = pin.s; posB = pin.pos; sujetoB = true; }
                }
                Vector3 e1 = ojal[0], ek = ojal[ojal.Count - 1];
                float faltaA = 0f, sobraA = u, faltaB = 0f, sobraB = largo - (u + D);
                if (sujetoA) { float x = (posA - e1).magnitude - (u - sA); faltaA = Mathf.Max(0f, x); sobraA = Mathf.Max(0f, -x); }
                if (sujetoB) { float x = (posB - ek).magnitude - (sB - (u + D)); faltaB = Mathf.Max(0f, x); sobraB = Mathf.Max(0f, -x); }
                if (faltaA > 0f && faltaB > 0f) exceso = faltaA + faltaB;
                else if (faltaA > 0f) { float da = Mathf.Min(faltaA, sobraB); u += da; exceso = faltaA - da; sobraB -= da; }
                else if (faltaB > 0f) { float da = Mathf.Min(faltaB, sobraA); u -= da; exceso = faltaB - da; sobraA -= da; }
                if (sujetoA && sujetoB && exceso <= 0f) holgura = sobraA + sobraB;
                if (!(sujetoA && sujetoB)) exceso = 0f;     // con un lado libre el hilo corre, no se tensa
            }

            // 3) colocar la cadena entre los puntos fijos (pinzas y ojales)
            todos.Clear();
            foreach (var pin in pins)
                if (ojal.Count == 0 || pin.s <= u || pin.s >= u + D) todos.Add(pin);
            float acum = 0f;
            for (int i = 0; i < ojal.Count; i++)
            {
                if (i > 0) acum += (ojal[i] - ojal[i - 1]).magnitude;
                todos.Add(new Pin { s = u + acum, pos = ojal[i] });
            }
            todos.Sort((x, y) => x.s.CompareTo(y.s));

            if (todos.Count == 0)
            {
                for (int k = 1; k < n; k++) Limitar(ref p[k], p[k - 1], seg);
                return;
            }
            if (ojal.Count == 0)
                for (int i = 1; i < todos.Count; i++)
                    exceso += Mathf.Max(0f, (todos[i].pos - todos[i - 1].pos).magnitude - (todos[i].s - todos[i - 1].s));

            foreach (var pin in todos)
            {
                int k = Mathf.RoundToInt(pin.s / seg);
                if (Mathf.Abs(k * seg - pin.s) < 1e-3f && k >= 0 && k < n) p[k] = pin.pos;
            }
            // antes del primero y después del último: un solo barrido hacia el extremo libre
            {
                var a = todos[0]; Vector3 prev = a.pos; float ps = a.s;
                for (int k = Mathf.CeilToInt(a.s / seg - 1e-3f) - 1; k >= 0; k--) { Limitar(ref p[k], prev, ps - k * seg); prev = p[k]; ps = k * seg; }
                var b = todos[todos.Count - 1]; prev = b.pos; ps = b.s;
                for (int k = Mathf.FloorToInt(b.s / seg + 1e-3f) + 1; k < n; k++) { Limitar(ref p[k], prev, k * seg - ps); prev = p[k]; ps = k * seg; }
            }
            // entre dos puntos fijos: ida y vuelta
            for (int i = 0; i < todos.Count - 1; i++)
            {
                var a = todos[i]; var b = todos[i + 1];
                int k0 = Mathf.FloorToInt(a.s / seg + 1e-3f) + 1, k1 = Mathf.CeilToInt(b.s / seg - 1e-3f) - 1;
                if (k1 < k0) continue;
                if ((b.pos - a.pos).magnitude >= (b.s - a.s) - 0.05f)
                {
                    // tramo tenso: línea recta entre los dos puntos fijos
                    for (int k = k0; k <= k1; k++) p[k] = Vector3.Lerp(a.pos, b.pos, (k * seg - a.s) / Mathf.Max(b.s - a.s, 1e-4f));
                    continue;
                }
                for (int it = 0; it < 6; it++)
                {
                    Vector3 prev = b.pos; float ps = b.s;
                    for (int k = k1; k >= k0; k--) { Limitar(ref p[k], prev, ps - k * seg); prev = p[k]; ps = k * seg; }
                    prev = a.pos; ps = a.s;
                    for (int k = k0; k <= k1; k++) { Limitar(ref p[k], prev, k * seg - ps); prev = p[k]; ps = k * seg; }
                }
            }
        }
    }

    /// <summary>Focos de sangrado: acumulan pérdida (ml) hasta que se controlan.</summary>
    public class Sangrado
    {
        public class Foco
        {
            public Vector3 p; public float mlMin, vol, compresion; public bool activo = true; public string nombre;
            public Transform vis; public Material mat;
        }
        public readonly List<Foco> focos = new List<Foco>();
        public float ml;
        public int controlados;
        readonly Transform raiz;

        public Sangrado(Transform raiz) { this.raiz = raiz; }

        public bool Activo { get { foreach (var f in focos) if (f.activo) return true; return false; } }

        public Foco Abrir(Vector3 p, float mlMin, string nombre)
        {
            var f = new Foco { p = p, mlMin = mlMin, nombre = nombre };
            f.mat = Geo.Mat(0xb00a12, 0f, 0.85f);
            f.vis = Geo.Sphere(1f, f.mat, raiz, p).transform;
            f.vis.name = "Sangrado";
            focos.Add(f);
            return f;
        }

        public void Cerrar(Foco f)
        {
            if (!f.activo) return;
            f.activo = false; controlados++;
            f.mat.color = Geo.Hex(0x4a1014);
            if (f.mat.HasProperty("_BaseColor")) f.mat.SetColor("_BaseColor", Geo.Hex(0x4a1014));
        }

        /// <param name="compresion">true = se controla comprimiendo 2 s con una pinza cerrada sobre el foco</param>
        public void Paso(float dt, Herr[] h, bool compresion = true)
        {
            foreach (var f in focos)
            {
                if (!f.activo) continue;
                float d = f.mlMin * dt / 60f;
                ml += d; f.vol += d;
                float s = Mathf.Min(13f, 2.2f + 2.4f * Mathf.Pow(f.vol, 1f / 3f)) * (1f + 0.06f * Mathf.Sin(Time.time * 9f));
                f.vis.localScale = new Vector3(s, s * 0.4f, s);
                bool comp = false;
                foreach (var t in h) if (t.cerrada && (t.pos - f.p).magnitude < 8f) comp = true;
                f.compresion = comp ? f.compresion + dt : 0f;
                if (compresion && f.compresion >= 2f) Cerrar(f);
            }
        }

        /// Compresión acumulada (0…1) del foco activo más avanzado, para la barra del panel.
        public float Progreso { get { float m = 0f; foreach (var f in focos) if (f.activo) m = Mathf.Max(m, f.compresion / 2f); return m; } }
    }

    public class Criterio
    {
        public string nombre, valor, consejo; public float perdida; public int dominio;
    }

    /// <summary>
    /// Base de un nivel: construye su escena en el marco de la anatomía, recibe el estado de los dos
    /// instrumentos en cada cuadro, lleva las métricas comunes (tiempo, economía de movimiento, choques,
    /// instrumentos fuera de la vista) y al terminar calcula el puntaje y la retroalimentación.
    /// </summary>
    public abstract class Nivel
    {
        public const int PRECISION = 0, BIMANUAL = 1, EFICIENCIA = 2, TEJIDO = 3, CONTROL = 4;
        public static readonly string[] DOMINIO = { "Precisión y profundidad", "Destreza bimanual", "Eficiencia", "Respeto del tejido", "Control del robot" };
        public static readonly string[] MANO = { "derecha", "izquierda" };
        static readonly Vector3 FOCO = new Vector3(15, -15, 0);     // punto del campo que queda al centro de la imagen

        public int numero; public string titulo = "", corto = "", intro = "";
        public string[] rol = { "agarre", "agarre" };
        public TipoInstrumento[] herramienta = { TipoInstrumento.Cadiere, TipoInstrumento.Cadiere };
        /// La pone BrazosApp: cambia la punta de un instrumento durante el ejercicio (p. ej. clips → tijera).
        public System.Action<int, TipoInstrumento> cambiarHerramienta;
        public Transform raiz; public Anatomia anat;
        public Vector3 origen = FOCO; public float inclinacion = 20f;
        public Orbita.Vista vista = new Orbita.Vista(Mathf.PI, 1.09f, 105, 42, FOCO);
        public bool iniciado, terminado; public float tiempo, puntaje;
        public float tiempoPar = 120f, recorridoPar = 150f;
        public readonly float[] recorrido = new float[2];
        public int colisiones; public float fueraVista; bool chocando;
        public string mensaje = ""; public float tMensaje; public bool mensajeMalo;
        public readonly List<Criterio> criterios = new List<Criterio>();
        public readonly float[] dominio = new float[5];
        public int dom;                                 // mano dominante (0 derecha, 1 izquierda)
        public static string archivo = "";
        /// Ayudas de profundidad activas (las pone BrazosApp); se guardan con el resultado para poder comparar.
        public static string ayudas = "";
        protected int DM { get { return dom; } }        // mano que corta / clipa / sutura
        protected int NM { get { return 1 - dom; } }    // mano que tracciona
        protected Transform esp;

        /// Coloca la anatomía para que el punto "roi" quede al centro del campo, y ajusta el endoscopio.
        protected void Centrar(Vector3 roi, float distancia, float fov)
        {
            Vector3 r = Quaternion.AngleAxis(inclinacion, Vector3.forward) * roi;
            origen = FOCO - r;
            vista = new Orbita.Vista(Mathf.PI, 1.09f, distancia, fov, FOCO);
        }

        public void Montar(Transform espInst, int manoDominante)
        {
            esp = espInst; dom = manoDominante;
            raiz = Geo.Grupo(espInst, "Nivel " + numero + " · " + corto, origen);
            raiz.localRotation = Quaternion.AngleAxis(inclinacion, Vector3.forward);
            Construir();
        }

        public virtual void Desmontar() { if (raiz != null) Util.Destruir(raiz.gameObject); raiz = null; }

        protected abstract void Construir();
        protected abstract void Paso(float dt, Herr[] h);
        /// Instrucciones, barras y métricas en vivo (se dibuja dentro del panel izquierdo).
        public abstract void Panel();
        protected abstract void Evaluar();
        /// Lo que debe seguir animándose aunque el ejercicio no haya empezado o ya haya terminado.
        protected virtual void Fondo(float dt, Herr[] h) { }

        public void Iniciar() { if (!terminado) iniciado = true; }

        public void Actualizar(float dt, Herr[] h, System.Func<Vector3, bool> enVista)
        {
            if (tMensaje > 0f) tMensaje -= dt;
            if (terminado) { Fondo(dt, h); return; }
            if (!iniciado)
            {
                if (h[0].cerro || h[1].cerro) iniciado = true;     // cerrar una pinza da la salida
                Fondo(dt, h);
                return;
            }
            tiempo += dt;
            for (int m = 0; m < 2; m++)
            {
                float d = (h[m].pos - h[m].previa).magnitude;
                if (d < 40f) recorrido[m] += d;
            }
            bool c = Mat3.SegSeg(h[0].muneca, h[0].punta, h[1].muneca, h[1].punta) < 1.2f
                  || Mat3.SegSeg(h[0].trocar, h[0].muneca, h[1].trocar, h[1].muneca) < 6f;
            if (c && !chocando) { colisiones++; Avisar("Choque entre los instrumentos", true); }
            chocando = c;
            if (enVista != null && (!enVista(h[0].pos) || !enVista(h[1].pos))) fueraVista += dt;
            Paso(dt, h);
            if (!terminado) Fondo(dt, h);
        }

        public void Avisar(string m, bool malo)
        {
            mensaje = m; tMensaje = 3.5f; mensajeMalo = malo;
        }

        protected void C(string nombre, string valor, float perdida, int dominioIdx, string consejo)
        {
            criterios.Add(new Criterio { nombre = nombre, valor = valor, perdida = Mathf.Max(0f, perdida), dominio = dominioIdx, consejo = consejo });
        }

        protected void Terminar()
        {
            if (terminado) return;
            terminado = true; criterios.Clear();
            Evaluar();
            C("Tiempo", tiempo.ToString("0") + " s (referencia " + tiempoPar.ToString("0") + " s)",
                Mathf.Min(15f, 0.25f * Mathf.Max(0f, tiempo - tiempoPar)), EFICIENCIA,
                "Planifica el movimiento antes de hacerlo: el tiempo baja al eliminar correcciones, no al acelerar.");
            float rec = (recorrido[0] + recorrido[1]) / 10f;
            C("Economía de movimiento", rec.ToString("0") + " cm (referencia " + recorridoPar.ToString("0") + " cm)",
                Mathf.Min(10f, 0.1f * Mathf.Max(0f, rec - recorridoPar)), EFICIENCIA,
                "Recorriste más distancia de la necesaria: lleva cada instrumento directo a su objetivo y usa el embrague para recolocar la mano.");
            C("Choques entre instrumentos", colisiones.ToString(), Mathf.Min(10f, 2f * colisiones), CONTROL,
                "Los instrumentos chocaron: separa los codos y deja que cada mano trabaje en su mitad del campo.");
            C("Instrumentos fuera de la vista", fueraVista.ToString("0.0") + " s", Mathf.Min(10f, fueraVista), CONTROL,
                "Mantén siempre las dos puntas dentro de la imagen: un instrumento que no se ve puede lesionar sin que lo notes.");
            float tot = Mathf.Max(1f, recorrido[0] + recorrido[1]);
            C("Reparto del movimiento", "derecha " + (100f * recorrido[0] / tot).ToString("0") + " % · izquierda " + (100f * recorrido[1] / tot).ToString("0") + " %", 0f, BIMANUAL, "");
            if (ayudas.Length > 0) C("Ayudas de profundidad", ayudas, 0f, PRECISION, "");

            float s = 100f; var perd = new float[5];
            foreach (var c in criterios) { s -= c.perdida; perd[c.dominio] += c.perdida; }
            puntaje = Mathf.Clamp(s, 0f, 100f);
            for (int i = 0; i < 5; i++) dominio[i] = Mathf.Clamp(5f - perd[i] / 5f, 1f, 5f);
            Guardar();
            Avisar("Ejercicio terminado: " + puntaje.ToString("0") + " / 100", false);
        }

        void Guardar()
        {
            try
            {
                archivo = System.IO.Path.Combine(Application.persistentDataPath, "BRAZOSrobot_resultados.csv");
                var sb = new System.Text.StringBuilder();
                if (!System.IO.File.Exists(archivo)) sb.AppendLine("fecha;nivel;ejercicio;puntaje;tiempo_s;detalle");
                sb.Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(';').Append(numero).Append(';').Append(titulo).Append(';')
                  .Append(puntaje.ToString("0.0")).Append(';').Append(tiempo.ToString("0.0")).Append(';');
                foreach (var c in criterios) sb.Append(c.nombre).Append('=').Append(c.valor).Append(" (-").Append(c.perdida.ToString("0.#")).Append(") | ");
                sb.AppendLine();
                System.IO.File.AppendAllText(archivo, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch (System.Exception) { archivo = ""; }
        }

        /// Puntaje, perfil por dominios, tabla de métricas y retroalimentación.
        public void PanelFinal()
        {
            Color col = puntaje >= 85f ? new Color(0.5f, 1f, 0.5f) : (puntaje >= 60f ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.5f, 0.45f));
            Hud.Tinte("Puntaje: " + puntaje.ToString("0") + " / 100", col, Hud.grande);
            GUILayout.Label("Perfil (1 a 5)", Hud.titulo);
            for (int i = 0; i < 5; i++)
            {
                GUILayout.Label(DOMINIO[i] + ": " + dominio[i].ToString("0.0"), Hud.dato);
                Hud.Barra(dominio[i] / 5f);
            }
            GUILayout.Label("Métricas", Hud.titulo);
            foreach (var c in criterios)
                Hud.Dato(c.nombre, c.valor + (c.perdida > 0.05f ? "   −" + c.perdida.ToString("0.#") : ""));
            GUILayout.Label("Retroalimentación", Hud.titulo);
            var orden = new List<Criterio>(criterios);
            orden.Sort((a, b) => b.perdida.CompareTo(a.perdida));
            int n = 0;
            foreach (var c in orden)
            {
                if (c.perdida <= 0.05f || string.IsNullOrEmpty(c.consejo) || n >= 4) continue;
                GUILayout.Label("• " + c.consejo, Hud.texto); n++;
            }
            if (n == 0) GUILayout.Label("Ejecución limpia: sin errores que corregir. Repite buscando el mismo resultado en menos tiempo y con menos recorrido.", Hud.texto);
            if (archivo.Length > 0) GUILayout.Label("Resultado guardado en " + archivo, Hud.nota);
        }

        protected void PanelMensaje()
        {
            if (tMensaje > 0f) Hud.Tinte(mensaje, mensajeMalo ? new Color(1f, 0.6f, 0.3f) : new Color(0.55f, 1f, 0.6f), Hud.titulo);
        }

        // ---------- piezas compartidas por varios niveles ----------

        /// Lazo vascular (vessel loop) que rodea el páncreas en el plano z = z0, con un cabo para tomarlo.
        protected Transform CrearLazo(float xc, float yc, float a, float b, float z0, out Vector3 asa)
        {
            var g = Geo.Grupo(raiz, "Lazo vascular", Vector3.zero);
            var m = Geo.Mat(0x2a8cf0, 0f, 0.6f);
            const int N = 16;
            var pts = new Vector3[N + 1];
            for (int i = 0; i <= N; i++)
            {
                float ang = i * 2f * Mathf.PI / N;
                pts[i] = new Vector3(xc + (a + 1.1f) * Mathf.Cos(ang), yc + (b + 1.1f) * Mathf.Sin(ang), z0);
            }
            Geo.Obj(new Tubo("banda", pts, 0.8f, 3).Malla(8, 0f, 0.3f, 0, 0), m, g, "banda");
            float q = 130f * Mathf.Deg2Rad;
            Vector3 nudo = new Vector3(xc + (a + 1.6f) * Mathf.Cos(q), yc + (b + 1.6f) * Mathf.Sin(q), z0);
            Vector3 fuera = new Vector3(Mathf.Cos(q), Mathf.Sin(q), 0f);
            asa = nudo + fuera * 6f + new Vector3(-1.5f, 0f, 0.8f);
            Geo.Obj(new Tubo("cabo", new[] { nudo - fuera, nudo + fuera * 3f, asa, asa + new Vector3(-3.5f, -0.5f, 2f) }, 0.8f, 4).Malla(8), m, g, "cabo");
            Geo.Sphere(1.7f, Geo.Mat(0x8cc8ff, 0f, 0.6f), g, nudo);
            Geo.Sphere(1.5f, Geo.Mat(0xffffff, 0f, 0.6f), g, asa);
            return g;
        }
    }
}
