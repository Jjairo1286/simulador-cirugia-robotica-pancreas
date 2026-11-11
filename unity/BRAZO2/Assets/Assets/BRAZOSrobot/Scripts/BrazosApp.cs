using System.Collections.Generic;
using UnityEngine;

namespace BrazosRobot
{
    /// <summary>
    /// BRAZOSrobot: dos mandos maestros de 6 ejes que teleoperan dos instrumentos robóticos de muñeca,
    /// con la misma lógica del visor HTML, y seis niveles de entrenamiento para cirugía robótica de
    /// páncreas: anillos, manipulación de tejido, trabajo bimanual, disección cerca de vasos, control de
    /// vasos pequeños y sutura. Cada nivel mide el desempeño y da retroalimentación en el panel izquierdo.
    ///
    /// Cadena de teleoperación (por mano):
    ///  1) cinemática directa del mando (producto de transformaciones homogéneas)
    ///  2) cambio de marco + escalado del desplazamiento + embrague
    ///  3) filtro de temblor y límite de velocidad
    ///  4) RCM: dirección del vástago y profundidad de inserción
    ///  5) IK de la muñeca: R_rel = R_vástago⁻¹ · R_mango → rotación, cabeceo, guiñada
    ///
    /// Toda la escena se modela en un espacio diestro en milímetros (igual que el visor HTML)
    /// colgado de un objeto con escala (0.001, 0.001, −0.001): así Unity la dibuja idéntica y la
    /// física trabaja en metros reales.
    /// </summary>
    public class BrazosApp : MonoBehaviour
    {
        const int CAPA_MANDOS = 30;
        const float PISO = -560f, OPEN = 60f;
        static readonly string[] NOMBRE = { "derecha", "izquierda" };
        static readonly float[] LIM = { 45, 35, 35, 80, 80, 180 };
        static readonly string[] EJE = { "Eje 1 · base", "Eje 2 · hombro", "Eje 3 · codo", "Eje 4 · ele azul", "Eje 5 · ele roja", "Eje 6 · giro del mango" };

        class Estado { public float[] q = new float[6]; public float g; }

        // escena
        Transform mundo, espMandos, espInst;
        readonly Mando[] mst = new Mando[2];
        readonly Instrumento[] ins = new Instrumento[2];
        readonly Lectura[] K = { new Lectura(), new Lectura() };
        readonly Pinza[] pinza = new Pinza[2];
        Practica practica;

        // niveles
        static readonly string[] NIVELES = { "1 Anillos", "2 Tejido", "3 Bimanual", "4 Disección", "5 Vasos", "6 Sutura" };
        Nivel nivel; int nivelIdx = -1, manoDom;
        readonly Herr[] herr = { new Herr { mano = 0 }, new Herr { mano = 1 } };
        bool herrLista, verNombres = true, verMandos, verVistas, verSerie = true;
        GameObject cavidad;
        Light sol, rel;
        Profundidad prof; bool verProf = true;
        UnityEngine.Rendering.AmbientMode ambModo0; Color ambLuz0;
        /// Para pruebas automáticas: permite sustituir el estado de los instrumentos antes de evaluar el nivel.
        public System.Action<Herr[], float> piloto;
        public Nivel NivelActual { get { return nivel; } }
        public Herr[] HerramientasActuales { get { return herr; } }
        public Practica PracticaAnillos { get { return practica; } }
        /// Para videos de demostración: si existe, manda a los instrumentos en lugar de los mandos.
        public System.Action<float> mandoExterno;
        public bool verRecuadroMandos = true;

        /// Lleva la punta de un instrumento (centro de las mordazas) a una pose dada en el marco de la
        /// anatomía del nivel, como si el cirujano la moviera con el mando: misma cadena esclava (filtro,
        /// límite de velocidad, RCM e IK de la muñeca).
        public void ComandarPunta(int m, Vector3 pos, Vector3 dir, Vector3 eje, bool cerrada, float dt)
        {
            var I = ins[m]; Transform r = nivel.raiz;
            Vector3 p0 = espInst.InverseTransformPoint(r.TransformPoint(pos));
            Vector3 x = (espInst.InverseTransformPoint(r.TransformPoint(pos + dir)) - p0).normalized;
            Vector3 y = espInst.InverseTransformPoint(r.TransformPoint(pos + eje)) - p0;
            y = (y - x * Vector3.Dot(y, x));
            if (y.sqrMagnitude < 1e-6f) y = Vector3.Cross(x, Mathf.Abs(x.y) < 0.9f ? Vector3.up : Vector3.forward);
            y.Normalize();
            I.Qc = Quaternion.LookRotation(Vector3.Cross(x, y), y);
            Vector3 actual = espInst.InverseTransformPoint(I.tipG.TransformPoint(new Vector3(I.largo * 0.55f, 0, 0)));
            I.Wc = p0 - (actual - I.W);
            S[m].g = Mathf.MoveTowards(S[m].g, cerrada ? 100f : 0f, 400f * dt);
        }

        public Camera CamaraInstrumentos { get { return camI; } }

        // teleoperación
        readonly Estado[] S = { new Estado(), new Estado() };
        readonly Vector3[] gimRest = new Vector3[2];
        // ejes del usuario (en el espacio de los mandos: mira hacia +Z, su derecha es −X)
        static readonly Vector3 U_R = new Vector3(-1, 0, 0), U_U = Vector3.up, U_F = Vector3.forward;
        // ejes de la imagen del endoscopio (la misma cámara que la vista "Endoscopio")
        Vector3 E_R, E_U, E_F;
        float escala = 0.2f;                        // 5 : 1 (mano : punta). Los giros van 1 : 1.
        const float ESC_MIN = 0.1f, ESC_MAX = 0.5f;
        static readonly float[] ESC_PRESET = { 0.1f, 0.2f, 1f / 3f, 0.5f };   // 10:1, 5:1, 3:1, 2:1
        Rect barraEscala;                           // barra de escala en pantalla (coordenadas del panel)
        bool embrague, embragueTecla, gripTecla, entradaOk = true;
        int act = 0;

        // cámaras
        Camera camI, camM;
        Orbita orbI, orbM;
        bool seguirMuneca;
        Dictionary<string, Orbita.Vista> vistasI, vistasM;
        int arrastre = -1; Vector3 mousePrev;

        // ESP32
        readonly EntradaSerie serie = new EntradaSerie();
        readonly float[,] raw = new float[2, 6]; readonly bool[,] tieneRaw = new bool[2, 6];
        readonly float[] rawG = new float[2]; readonly bool[] tieneG = new bool[2];
        readonly float?[,] cero = new float?[2, 6];
        readonly float[,] signo = new float[2, 6];
        string[] puertos = new string[0]; int puertoSel; string baudios = "115200";

        // panel
        Vector2 scroll; float ui = 1f; const float PANEL = 350f;
        GUIStyle titulo, nota, aviso;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Arrancar()
        {
#if UNITY_2023_1_OR_NEWER
            if (FindFirstObjectByType<BrazosApp>() == null)
#else
            if (FindObjectOfType<BrazosApp>() == null)
#endif
                new GameObject("BRAZOSrobot").AddComponent<BrazosApp>();
        }

        void Start()
        {
            // física para piezas de pocos milímetros
            Time.fixedDeltaTime = 0.005f;
            Physics.defaultContactOffset = 0.0003f;
            Physics.defaultSolverIterations = 20;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.sleepThreshold = 0.00005f;          // que las piezas livianas no se "duerman" al moverse lento
            QualitySettings.shadowDistance = 3f;

            foreach (var c in Camera.allCameras) c.gameObject.SetActive(false);
            for (int m = 0; m < 2; m++) for (int j = 0; j < 6; j++)
                    signo[m, j] = PlayerPrefs.GetInt("signo" + m + j, 1) < 0 ? -1f : 1f;

            ambModo0 = RenderSettings.ambientMode; ambLuz0 = RenderSettings.ambientLight;
            Construir();
            Camaras();
            prof = new Profundidad(camI, espInst, ins, sol, rel);
            manoDom = PlayerPrefs.GetInt("manoDom", 0);
            verNombres = PlayerPrefs.GetInt("nombres", 1) == 1;
            CambiarNivel(PlayerPrefs.GetInt("nivel", 0));
            puertos = EntradaSerie.Puertos();
            string guardado = PlayerPrefs.GetString("puerto", "");
            for (int i = 0; i < puertos.Length; i++) if (puertos[i] == guardado) puertoSel = i;
        }

        void OnDestroy() { serie.Desconectar(); }

        // ===================== escena =====================
        void Construir()
        {
            mundo = new GameObject("Escena (mm, espacio diestro)").transform;
            mundo.localScale = new Vector3(0.001f, 0.001f, -0.001f);

            sol = new GameObject("Luz principal").AddComponent<Light>();
            sol.type = LightType.Directional; sol.intensity = 1.1f; sol.shadows = LightShadows.Soft;
            sol.shadowBias = 0.02f; sol.shadowNormalBias = 0.1f;
            sol.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            rel = new GameObject("Luz de relleno").AddComponent<Light>();
            rel.type = LightType.Directional; rel.intensity = 0.35f;
            rel.transform.rotation = Quaternion.Euler(-25f, 150f, 0f);

            // ---- mandos (aparte, a 5 m; los ve solo su cámara) ----
            espMandos = Geo.Grupo(mundo, "Mandos", new Vector3(0, 0, 5000));
            espMandos.gameObject.layer = CAPA_MANDOS;
            Geo.Box(new Vector3(2400, 2, 2400), Geo.Mat(0xcfd8dd, 0f, 0.2f), espMandos, new Vector3(0, -1, 0));
            mst[0] = new Mando(espMandos, -330, false, Geo.Hex(0x2e9e5e));
            mst[1] = new Mando(espMandos, 330, true, Geo.Hex(0x7a55d0));
            for (int m = 0; m < 2; m++) { Vector3 a, b; mst[m].Mango(out gimRest[m], out a, out b); }
            Geo.SetLayer(espMandos.gameObject, CAPA_MANDOS);

            // ---- lado del paciente ----
            espInst = Geo.Grupo(mundo, "Instrumentos", Vector3.zero);
            var torso = Geo.Box(new Vector3(520, 110, 330), Geo.Transparente(0xe2a48f, 0.12f), espInst, new Vector3(-20, 5, 0));
            var pared = Geo.Box(new Vector3(240, 1.5f, 300), Geo.Transparente(0xd08a80, 0.18f), espInst, new Vector3(-110, 60, 0));
            torso.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pared.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Geo.Box(new Vector3(760, 22, 360), Geo.Mat(0x4a555d, 0.3f, 0.5f), espInst, new Vector3(-10, -61, 0));
            Geo.CylY(65, -72 - PISO, Geo.Mat(0x3a444b, 0.3f, 0.5f), espInst, new Vector3(-10, (PISO - 72) / 2f, 0));
            Geo.Box(new Vector3(2600, 4, 2600), Geo.Mat(0xc9d1d6, 0f, 0.3f), espInst, new Vector3(0, PISO - 2, 0));

            // +Z de la escena = derecha del usuario; los trócares están separados y los vástagos convergen
            ins[0] = new Instrumento(espInst, Geo.Hex(0x2e9e5e), new Vector3(-110, 60, 115), new Vector3(0, 0, 32), -430, 430, new Vector3(-0.3f, 1, 0.6f), PISO);
            ins[1] = new Instrumento(espInst, Geo.Hex(0x7a55d0), new Vector3(-110, 60, -115), new Vector3(0, 0, -32), -430, -430, new Vector3(-0.3f, 1, -0.6f), PISO);
            Instrumento.Endoscopio(espInst, new Vector3(-110, 60, 0), new Vector3(5, 0, 0), PISO);

            practica = new Practica(espInst);
            pinza[0] = new Pinza(ins[0], practica.raiz, "Pinza derecha");
            pinza[1] = new Pinza(ins[1], practica.raiz, "Pinza izquierda");
            pinza[0].otra = pinza[1]; pinza[1].otra = pinza[0];

            // pared de la cavidad abdominal: solo se ve desde dentro (niveles con anatomía)
            cavidad = Geo.Obj(Mallas.Elipsoide(new Vector3(10, 2, 0), new Vector3(175, 80, 185), 32, 18, 0f, true), Geo.Mat(0x6b302b, 0f, 0.25f), espInst, "Cavidad abdominal");
            cavidad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // marco de la cámara del endoscopio: todo el mando se refiere a esta imagen
            Vector3 mira = new Vector3(5, 0, 0);
            Vector3 camPos = mira + 100f * new Vector3(Mathf.Sin(1.09f) * Mathf.Cos(Mathf.PI), Mathf.Cos(1.09f), Mathf.Sin(1.09f) * Mathf.Sin(Mathf.PI));
            E_F = (mira - camPos).normalized;
            E_R = Vector3.Cross(E_F, Vector3.up).normalized;
            E_U = Vector3.Cross(E_R, E_F);
            for (int m = 0; m < 2; m++) { Maestro(m); ins[m].W = ins[m].Wc; ins[m].Q = ins[m].Qc; }
        }

        void Camaras()
        {
            const float H = Mathf.PI / 2f;
            vistasI = new Dictionary<string, Orbita.Vista> {
                { "Endoscopio",     new Orbita.Vista(Mathf.PI, 1.09f, 105, 46, new Vector3(10, -8, 0)) },
                { "Robot completo", new Orbita.Vista(0.75f, 1.0f, 1700, 40, new Vector3(-250, -40, 0)) },
                { "Lateral",        new Orbita.Vista(H, 1.35f, 1150, 40, new Vector3(-250, -60, 0)) },
                { "Superior",       new Orbita.Vista(Mathf.PI, 0.02f, 1500, 40, new Vector3(-250, 0, 0)) },
                { "Muñeca",         new Orbita.Vista(2.4f, 1.1f, 90, 40) } };
            vistasM = new Dictionary<string, Orbita.Vista> {
                { "Desde el usuario", new Orbita.Vista(-H, 1.3f, 1350, 35, new Vector3(0, 220, -90)) },
                { "Isométrica",       new Orbita.Vista(-1.95f, 1.2f, 1400, 35, new Vector3(0, 220, -90)) },
                { "Superior",         new Orbita.Vista(-H, 0.02f, 1400, 35, new Vector3(0, 220, -90)) } };

            camI = new GameObject("Cámara instrumentos").AddComponent<Camera>();
            camI.nearClipPlane = 0.002f; camI.farClipPlane = 30f;
            camI.clearFlags = CameraClearFlags.SolidColor; camI.backgroundColor = Geo.Hex(0xdde4e8);
            camI.cullingMask = ~(1 << CAPA_MANDOS);
            camI.gameObject.AddComponent<AudioListener>();
            orbI = new Orbita(camI, espInst, vistasI["Endoscopio"], 30, 3600);

            camM = new GameObject("Cámara mandos").AddComponent<Camera>();
            camM.nearClipPlane = 0.01f; camM.farClipPlane = 30f; camM.depth = 1;
            camM.rect = new Rect(0.70f, 0.60f, 0.295f, 0.385f);
            camM.clearFlags = CameraClearFlags.SolidColor; camM.backgroundColor = Geo.Hex(0xeef2f4);
            camM.cullingMask = 1 << CAPA_MANDOS;
            orbM = new Orbita(camM, espMandos, vistasM["Isométrica"], 150, 3200);
        }

        // ===================== ciclo =====================
        static float Wrap180(float a) { return Mathf.Repeat(a + 180f, 360f) - 180f; }
        float Apertura(int m) { return ins[m].aperturaMax * (1f - S[m].g / 100f); }
        /// Apertura en la escala de 60° que usa la física de las pinzas del nivel 1
        float Apertura60(int m) { return OPEN * (1f - S[m].g / 100f); }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (entradaOk)
            {
                try { Teclado(dt); }
                catch (System.InvalidOperationException) { entradaOk = false; }   // proyecto solo con el Input System nuevo
            }
            EnVivo();
            if (mandoExterno != null) mandoExterno(dt);
            else for (int m = 0; m < 2; m++) Maestro(m);
            float a = 1f - Mathf.Exp(-dt / Instrumento.TAU);
            for (int m = 0; m < 2; m++) ins[m].Paso(a, dt, Apertura(m), K[m]);
            if (nivel != null)
            {
                Herramientas(dt);
                if (piloto != null) piloto(herr, dt);
                nivel.Actualizar(dt, herr, EnVista);
            }
            if (entradaOk) { try { Raton(); } catch (System.InvalidOperationException) { entradaOk = false; } }
            if (seguirMuneca) orbI.objetivo = ins[act].W;
            camM.enabled = verRecuadroMandos;
            orbI.Actualizar(dt); orbM.Actualizar(dt);
            serie.ContarHz(dt);
        }

        // después de mover cámara e instrumentos: luces, indicador de distancia y cámara del ojo derecho
        void LateUpdate()
        {
            if (prof != null) prof.Actualizar(Mathf.Min(Time.deltaTime, 0.05f), orbI.objetivo, nivelIdx > 0);
        }

        void FixedUpdate()
        {
            if (nivel is NivelAnillos)
                for (int m = 0; m < 2; m++) pinza[m].Seguir(Apertura60(m));
        }

        // ===================== niveles =====================
        public void CambiarNivel(int i)
        {
            i = Mathf.Clamp(i, 0, NIVELES.Length - 1);
            if (nivel != null) nivel.Desmontar();
            switch (i)
            {
                case 1: nivel = new NivelTejido(); break;
                case 2: nivel = new NivelBimanual(); break;
                case 3: nivel = new NivelDiseccion(); break;
                case 4: nivel = new NivelVasos(); break;
                case 5: nivel = new NivelSutura(); break;
                default: nivel = new NivelAnillos(practica, pinza); break;
            }
            nivelIdx = i; herrLista = false;
            PlayerPrefs.SetInt("nivel", i);
            nivel.Montar(espInst, manoDom);
            if (prof != null) prof.NuevoNivel(i > 0 ? nivel.raiz : practica.raiz);
            cavidad.SetActive(i > 0);
            if (i > 0) practica.raiz.gameObject.SetActive(false);    // el tablero de anillos solo en el nivel 1
            // dentro del abdomen solo ilumina el endoscopio: luz ambiente baja para que los colores no se laven
            RenderSettings.ambientMode = i > 0 ? UnityEngine.Rendering.AmbientMode.Flat : ambModo0;
            RenderSettings.ambientLight = i > 0 ? new Color(0.30f, 0.25f, 0.24f) : ambLuz0;
            for (int m = 0; m < 2; m++) ins[m].PonerTipo(nivel.herramienta[m]);
            nivel.cambiarHerramienta = (m, t) => { if (ins[m].tipo != t) ins[m].PonerTipo(t); };
            if (i == 0) for (int m = 0; m < 2; m++) pinza[m].Seguir(Apertura60(m), true);
            vistasI["Endoscopio"] = nivel.vista;
            seguirMuneca = false;
            orbI.Ir(nivel.vista);
            scroll = Vector2.zero;
        }

        /// Estado de cada instrumento en el marco de la anatomía del nivel (mm).
        void Herramientas(float dt)
        {
            Transform r = nivel.raiz;
            for (int m = 0; m < 2; m++)
            {
                var I = ins[m]; var t = herr[m];
                t.previa = t.pos;
                Vector3 o = r.InverseTransformPoint(I.tipG.position);
                t.pos = r.InverseTransformPoint(I.tipG.TransformPoint(new Vector3(I.largo * 0.55f, 0, 0)));
                t.punta = r.InverseTransformPoint(I.tipG.TransformPoint(new Vector3(I.largo, 0, 0)));
                t.dir = (t.punta - o).normalized;
                t.eje = (r.InverseTransformPoint(I.tipG.TransformPoint(new Vector3(0, 1, 0))) - o).normalized;
                t.muneca = r.InverseTransformPoint(I.pitchG.position);
                t.trocar = r.InverseTransformPoint(espInst.TransformPoint(I.T));
                t.ap = Apertura(m);
                bool antes = t.cerrada;
                float fr = t.ap / Mathf.Max(1f, I.aperturaMax);   // 0 = cerrada, 1 = abierta del todo
                if (fr < 0.37f) t.cerrada = true; else if (fr > 0.5f) t.cerrada = false;
                t.cerro = t.cerrada && !antes; t.abrio = !t.cerrada && antes;
                if (!herrLista) { t.previa = t.pos; t.rapidez = 0f; t.cerro = t.abrio = false; }
                float v = (t.pos - t.previa).magnitude / Mathf.Max(dt, 1e-4f);
                t.rapidez = Mathf.Lerp(t.rapidez, v, 1f - Mathf.Exp(-dt / 0.08f));
            }
            herrLista = true;
        }

        /// ¿El punto (marco de la anatomía) cae dentro de la imagen del endoscopio del nivel?
        bool EnVista(Vector3 pAnat)
        {
            Vector3 pe = espInst.InverseTransformPoint(nivel.raiz.TransformPoint(pAnat));
            var v = nivel.vista; Vector3 mira = v.mira ?? Vector3.zero;
            Vector3 cam = mira + v.r * new Vector3(Mathf.Sin(v.ph) * Mathf.Cos(v.th), Mathf.Cos(v.ph), Mathf.Sin(v.ph) * Mathf.Sin(v.th));
            Vector3 d = pe - cam; float f = Vector3.Dot(d, E_F);
            if (f <= 1f) return false;
            float hv = v.fov * 0.5f, hh = Mathf.Atan(Mathf.Tan(hv * Mathf.Deg2Rad) * 1.45f) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.Atan2(Vector3.Dot(d, E_U), f)) * Mathf.Rad2Deg < hv
                && Mathf.Abs(Mathf.Atan2(Vector3.Dot(d, E_R), f)) * Mathf.Rad2Deg < hh;
        }

        /// Vector del marco del usuario (espacio de los mandos) al marco de la cámara del endoscopio
        Vector3 ACamara(Vector3 v)
        {
            return E_R * Vector3.Dot(v, U_R) + E_U * Vector3.Dot(v, U_U) + E_F * Vector3.Dot(v, U_F);
        }

        /// Teleoperación como en el da Vinci / Hugo RAS:
        ///  • todo se refiere a la cámara del endoscopio (mano a la derecha → punta a la derecha en pantalla)
        ///  • posición RELATIVA y escalada del CENTRO DE LA MUÑECA (se puede embragar): girar la mano
        ///    solo mueve la muñeca y el vástago no pivota en el trócar
        ///  • orientación ABSOLUTA: la punta apunta y rota en la imagen igual que la pistola vista por el usuario
        void Maestro(int m)
        {
            var s = S[m]; var I = ins[m];
            mst[m].Poner(s.q, s.g);
            Vector3 pos, eje, arriba;
            mst[m].Mango(out pos, out eje, out arriba);            // cinemática directa de los 6 ejes
            I.P = pos - gimRest[m];
            Vector3 x = ACamara(eje).normalized, y = ACamara(arriba);
            y = (y - x * Vector3.Dot(y, x)).normalized;
            Vector3 z = Vector3.Cross(x, y);
            I.Qm = Quaternion.LookRotation(z, y);                  // base (x, y, z) como rotación
            if (embrague) return;                                  // embrague pisado: la orden se congela
            I.Wc = ACamara(I.P - I.Pa) * escala + I.Wa;
            I.Qc = I.Qm;
        }

        void PonerEmbrague(bool on)
        {
            if (embrague == on) return;
            embrague = on;
            if (!on)
                foreach (var I in ins) { I.Pa = I.P; I.Wa = I.Wc; }   // la orientación es absoluta: no se embraga
        }

        void Anclas()
        {
            foreach (var I in ins) { I.Pa = Vector3.zero; I.Wa = I.Wrest; }
        }

        void ReiniciarMandos()
        {
            PonerEmbrague(false);
            foreach (var s in S) { System.Array.Clear(s.q, 0, 6); s.g = 0; }
            Anclas();
        }

        // ---------- teclado ----------
        static readonly KeyCode[] MAS = { KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T, KeyCode.Y };
        static readonly KeyCode[] MENOS = { KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F, KeyCode.G, KeyCode.H };

        void Teclado(float dt)
        {
            if (GUIUtility.keyboardControl != 0) return;            // escribiendo en un campo del panel
            if (Input.GetKeyDown(KeyCode.Tab)) act = 1 - act;
            for (int i = 0; i < NIVELES.Length; i++) if (Input.GetKeyDown(KeyCode.F1 + i)) CambiarNivel(i);
            if (Input.GetKeyDown(KeyCode.N)) { verNombres = !verNombres; PlayerPrefs.SetInt("nombres", verNombres ? 1 : 0); }
            if (Input.GetKeyDown(KeyCode.Return) && nivel != null) nivel.Iniciar();
            if (Input.GetKeyDown(KeyCode.P)) { prof.indicador = !prof.indicador; prof.Guardar(); }
            if (Input.GetKeyDown(KeyCode.V)) { prof.estereo = !prof.estereo; prof.Guardar(); }
            // + más rápido (2 : 1) · − más fino (10 : 1)
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) PonerEscala(escala + 0.05f);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) PonerEscala(escala - 0.05f);
            bool c = Input.GetKey(KeyCode.C);
            if (c != embragueTecla) { embragueTecla = c; PonerEmbrague(c); }
            if (serie.conectado) return;                            // con la ESP32 mandan los encoders
            var s = S[act];
            for (int j = 0; j < 6; j++)
            {
                float v = (Input.GetKey(MAS[j]) ? 1 : 0) - (Input.GetKey(MENOS[j]) ? 1 : 0);
                if (v != 0) s.q[j] = Mathf.Clamp(s.q[j] + v * (j == 5 ? 120f : 45f) * dt, -LIM[j], LIM[j]);
            }
            if (Input.GetKey(KeyCode.Space)) { gripTecla = true; s.g = Mathf.MoveTowards(s.g, 100, 400 * dt); }
            else if (gripTecla) { s.g = Mathf.MoveTowards(s.g, 0, 400 * dt); if (s.g <= 0) gripTecla = false; }
        }

        // ---------- ESP32 ----------
        void EnVivo()
        {
            if (!serie.conectado) return;
            serie.Tomar(raw, tieneRaw, rawG, tieneG);
            for (int m = 0; m < 2; m++)
            {
                for (int j = 0; j < 6; j++)
                {
                    if (!tieneRaw[m, j]) continue;
                    if (!cero[m, j].HasValue) cero[m, j] = raw[m, j];
                    float d = Wrap180(raw[m, j] - cero[m, j].Value) * signo[m, j];
                    float e = Wrap180(d - S[m].q[j]);
                    S[m].q[j] = Mathf.Abs(e) < 0.05f ? d : Wrap180(S[m].q[j] + e * 0.5f);
                }
                if (tieneG[m]) S[m].g += (rawG[m] - S[m].g) * 0.5f;   // 0 % suelto = pinza abierta
            }
        }

        void PonerCero()
        {
            serie.Tomar(raw, tieneRaw, rawG, tieneG);
            for (int m = 0; m < 2; m++) for (int j = 0; j < 6; j++)
                    cero[m, j] = tieneRaw[m, j] ? raw[m, j] : (float?)null;
            Anclas();
        }

        // ---------- ratón: orbitar y acercar ----------
        void PonerEscala(float v) { escala = Mathf.Clamp(Mathf.Round(v * 100f) / 100f, ESC_MIN, ESC_MAX); }
        static string Proporcion(float e) { float r = 1f / e; return (Mathf.Abs(r - Mathf.Round(r)) < 0.05f ? Mathf.Round(r).ToString("0") : r.ToString("0.0")) + " : 1"; }

        void Raton()
        {
            Vector3 mp = Input.mousePosition;
            Vector2 gp = new Vector2(mp.x / ui, (Screen.height - mp.y) / ui);
            bool sobrePanel = mp.x < (PANEL + 16) * ui || barraEscala.Contains(gp);
            int bajo = camM.pixelRect.Contains(mp) ? 1 : 0;
            if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !sobrePanel)
            {
                arrastre = bajo; mousePrev = mp;
                GUIUtility.keyboardControl = 0;    // devuelve el teclado a los mandos
            }
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1)) arrastre = -1;
            if (arrastre >= 0)
            {
                Vector2 d = mp - mousePrev; mousePrev = mp;
                (arrastre == 1 ? orbM : orbI).Arrastrar(d);
            }
            float rueda = Input.mouseScrollDelta.y;
            if (rueda != 0 && !sobrePanel) (bajo == 1 ? orbM : orbI).Zoom(rueda);
        }

        // ===================== panel =====================
        void Estilos()
        {
            if (titulo != null) return;
            titulo = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 15 };
            nota = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
            aviso = new GUIStyle(nota); aviso.normal.textColor = new Color(1f, 0.55f, 0.45f);
        }

        void OnGUI()
        {
            if (ins[0] == null || nivel == null) return;
            Estilos(); Hud.Estilos();
            ui = Mathf.Max(1f, Screen.height / 1000f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(ui, ui, 1));
            float alto = Screen.height / ui;
            GUILayout.BeginArea(new Rect(8, 8, PANEL, alto - 16), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("BRAZOSrobot · cirugía robótica de páncreas", titulo);
            if (!entradaOk) GUILayout.Label("El teclado y el ratón están desactivados: en Edit > Project Settings > Player > Active Input Handling elige \"Both\" y reinicia Unity.", aviso);
            int ni = GUILayout.SelectionGrid(nivelIdx, NIVELES, 3);
            if (ni != nivelIdx) { CambiarNivel(ni); GUIUtility.ExitGUI(); }

            // ---------- ejercicio ----------
            GUILayout.Space(4);
            GUILayout.Label("Nivel " + nivel.numero + " — " + nivel.titulo, Hud.titulo);
            if (nivel.terminado) nivel.PanelFinal();
            else
            {
                nivel.Panel();
                if (!nivel.iniciado)
                {
                    GUILayout.Label("Mano derecha: " + Catalogo.Nombre(nivel.herramienta[0]) + " — " + nivel.rol[0] + "\nMano izquierda: " + Catalogo.Nombre(nivel.herramienta[1]) + " — " + nivel.rol[1], Hud.nota);
                    Hud.Tinte("Cierra cualquier pinza (o pulsa Empezar) para iniciar el cronómetro.", new Color(0.55f, 1f, 0.6f), Hud.texto);
                    if (GUILayout.Button("Empezar")) nivel.Iniciar();
                }
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(nivel.terminado ? "Repetir" : "Reiniciar ejercicio")) { CambiarNivel(nivelIdx); GUIUtility.ExitGUI(); }
            if (nivel.terminado && nivelIdx < NIVELES.Length - 1 && GUILayout.Button("Siguiente nivel")) { CambiarNivel(nivelIdx + 1); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            bool vn = GUILayout.Toggle(verNombres, " Nombres anatómicos (N)");
            if (vn != verNombres) { verNombres = vn; PlayerPrefs.SetInt("nombres", vn ? 1 : 0); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mano dominante:", nota, GUILayout.Width(100));
            int md = GUILayout.Toolbar(manoDom, new[] { "derecha", "izquierda" });
            if (md != manoDom) { manoDom = md; PlayerPrefs.SetInt("manoDom", md); CambiarNivel(nivelIdx); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();

            // ---------- avisos del robot ----------
            for (int m = 0; m < 2; m++)
            {
                if (K[m].muneca) GUILayout.Label("Instrumento " + NOMBRE[m] + ": la muñeca llegó a su límite de 90°.", aviso);
                if (K[m].tope) GUILayout.Label("Instrumento " + NOMBRE[m] + ": el carro llegó al tope del riel (40–240 mm). Usa el embrague para recolocar la mano.", aviso);
            }
            GUILayout.BeginHorizontal();
            bool emb = GUILayout.Toggle(embrague, embrague ? " EMBRAGUE PISADO" : " Embrague (C)", "Button");
            if (emb != embrague) PonerEmbrague(emb);
            if (GUILayout.Button("Reiniciar mandos")) ReiniciarMandos();
            GUILayout.EndHorizontal();

            // ---------- vistas ----------
            GUILayout.Space(4);
            verVistas = GUILayout.Toggle(verVistas, (verVistas ? "▼" : "►") + " Vistas", "Button");
            if (verVistas)
            {
                GUILayout.Label("Vista de los instrumentos", titulo);
                Botones(vistasI, orbI, true);
                GUILayout.Label("Vista de los mandos (recuadro)", titulo);
                Botones(vistasM, orbM, false);
            }

            // ---------- profundidad ----------
            verProf = GUILayout.Toggle(verProf, (verProf ? "▼" : "►") + " Percepción de profundidad", "Button");
            if (verProf)
            {
                bool a = GUILayout.Toggle(prof.luzEndo, " Luz del endoscopio (lo cercano se ve más claro)");
                bool b = GUILayout.Toggle(prof.sombras, " Sombras de las puntas (luz lateral)");
                bool c = GUILayout.Toggle(prof.indicador, " Indicador de distancia al tejido (P)");
                bool d = GUILayout.Toggle(prof.estereo, " 3D con lentes rojo-cian (V)");
                if (a != prof.luzEndo || b != prof.sombras || c != prof.indicador || d != prof.estereo)
                { prof.luzEndo = a; prof.sombras = b; prof.indicador = c; prof.estereo = d; prof.Guardar(); }
                if (prof.estereo)
                {
                    GUILayout.Label("Separación de los ojos: " + prof.separacion.ToString("0.0") + " mm (más = más relieve)", nota);
                    float sp = GUILayout.HorizontalSlider(prof.separacion, 1f, 10f);
                    if (Mathf.Abs(sp - prof.separacion) > 0.01f) { prof.separacion = sp; prof.Guardar(); }
                }
                GUILayout.Label("Punta y sombra se juntan al tocar el tejido. El anillo marca el tejido que está detrás de la punta y se cierra al acercarse; en rojo hay contacto. Es una ayuda de entrenamiento: ve quitándola a medida que ganes práctica.", nota);
            }

            // ---------- mandos ----------
            verMandos = GUILayout.Toggle(verMandos, (verMandos ? "▼" : "►") + " Mandos y ejes", "Button");
            if (verMandos)
            {
                act = GUILayout.Toolbar(act, new[] { "Mano derecha", "Mano izquierda" });
                var s = S[act];
                GUILayout.Label("Mando " + NOMBRE[act] + (serie.conectado ? " (lo mueven los encoders)" : ""), titulo);
                for (int j = 0; j < 6; j++)
                {
                    GUILayout.Label(EJE[j] + ": " + s.q[j].ToString("0") + "°");
                    float v = GUILayout.HorizontalSlider(s.q[j], -LIM[j], LIM[j]);
                    if (!serie.conectado) s.q[j] = v;
                }
                GUILayout.Label("Gatillo: " + s.g.ToString("0") + " % presionado (pinza " + Apertura(act).ToString("0") + "°)");
                float gv = GUILayout.HorizontalSlider(s.g, 0, 100);
                if (!serie.conectado) s.g = gv;
                GUILayout.Label("Escala de movimiento (mano : punta): " + Proporcion(escala));
                PonerEscala(GUILayout.HorizontalSlider(escala, ESC_MIN, ESC_MAX));
                GUILayout.Label("La posición se reduce con la escala; los giros de la muñeca van 1 : 1.", nota);
                var k = K[act]; var I = ins[act];
                GUILayout.Label("Instrumento " + NOMBRE[act], titulo);
                GUILayout.Label("Inclinación " + k.inc.ToString("0.0") + "°   Giro lateral " + k.lat.ToString("0.0") + "°   Inserción " + k.ins.ToString("0") + " mm");
                GUILayout.Label("Rotación " + I.roll.ToString("0") + "°   Cabeceo " + I.pitch.ToString("0") + "°   Guiñada " + I.yaw.ToString("0") + "°");
                if (pinza[act].sujeta != null) GUILayout.Label("Sujetando: " + pinza[act].sujeta.name);
            }

            // ---------- ESP32 ----------
            verSerie = GUILayout.Toggle(verSerie, (verSerie ? "▼" : "►") + " ESP32 (puerto serie)", "Button");
            if (verSerie)
            {
                GUILayout.BeginHorizontal();
                string nombrePuerto = puertos.Length > 0 ? puertos[Mathf.Clamp(puertoSel, 0, puertos.Length - 1)] : "(sin puertos)";
                if (GUILayout.Button(nombrePuerto, GUILayout.Width(150)) && puertos.Length > 0) puertoSel = (puertoSel + 1) % puertos.Length;
                if (GUILayout.Button("Buscar")) puertos = EntradaSerie.Puertos();
                baudios = GUILayout.TextField(baudios, GUILayout.Width(70));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (!serie.conectado)
                {
                    if (GUILayout.Button("Conectar"))
                    {
                        int b; if (!int.TryParse(baudios, out b)) b = 115200;
                        for (int m = 0; m < 2; m++) for (int j = 0; j < 6; j++) cero[m, j] = null;
                        serie.Limpiar();
                        serie.Conectar(nombrePuerto, b);
                        PlayerPrefs.SetString("puerto", nombrePuerto);
                        Anclas();
                    }
                }
                else if (GUILayout.Button("Desconectar")) serie.Desconectar();
                GUI.enabled = serie.conectado;
                if (GUILayout.Button("Poner en cero aquí")) PonerCero();
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Label(serie.estado + (serie.conectado ? "  ·  " + serie.hz + " Hz" : ""), nota);
                if (serie.ultimaLinea.Length > 0) GUILayout.Label(serie.ultimaLinea, nota);
                serie.primerosSonDerecha = GUILayout.Toggle(serie.primerosSonDerecha, " Los primeros 7 datos son de la mano derecha");
                GUILayout.Label("Invertir sentido (mano " + NOMBRE[act] + "):", nota);
                GUILayout.BeginHorizontal();
                for (int j = 0; j < 6; j++)
                {
                    bool inv = GUILayout.Toggle(signo[act, j] < 0, (j + 1).ToString());
                    float sg = inv ? -1f : 1f;
                    if (sg != signo[act, j]) { signo[act, j] = sg; PlayerPrefs.SetInt("signo" + act + j, (int)sg); }
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("Al conectar, la posición de cada mando se toma como 0°. Deja los mandos quietos en su posición de reposo.", nota);
            }

            GUILayout.Space(6);
            GUILayout.Label("Teclas: F1–F6 nivel · Intro empezar · N nombres · P indicador de distancia · V 3D · C embrague (mantener) · + / − escala. Sin ESP32: Q/A eje 1 · W/S eje 2 · E/D eje 3 · R/F eje 4 · T/G eje 5 · Y/H eje 6 · Espacio cierra la pinza · Tab cambia de mano. Ratón: arrastra para girar la vista y usa la rueda para acercar.", nota);

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            BarraEscala(alto);
            Sobreimpresion();
        }

        /// Nombres anatómicos sobre la imagen y avisos grandes del ejercicio.
        void Sobreimpresion()
        {
            GUI.matrix = Matrix4x4.identity;
            float x0 = (PANEL + 16) * ui;
            Rect recuadro = camM.pixelRect;
            if (verNombres && nivel.anat != null && nivel.raiz != null)
            {
                Hud.etiqueta.fontSize = Mathf.RoundToInt(11 * ui);
                foreach (var e in nivel.anat.etiquetas)
                {
                    Vector3 sp = camI.WorldToScreenPoint(nivel.raiz.TransformPoint(e.p));
                    if (sp.z <= 0f || sp.x < x0 || sp.x > Screen.width - 40 || sp.y < 80 * ui || sp.y > Screen.height - 10) continue;
                    if (recuadro.Contains(new Vector2(sp.x, sp.y))) continue;
                    float gx = sp.x, gy = Screen.height - sp.y;
                    GUI.color = new Color(1f, 1f, 1f, 0.9f);
                    GUI.DrawTexture(new Rect(gx - 2, gy - 2, 4, 4), Texture2D.whiteTexture);
                    var r = new Rect(gx + 6, gy - 9 * ui, 330 * ui, 20 * ui);
                    GUI.color = new Color(0f, 0f, 0f, 0.85f);
                    GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), e.texto, Hud.etiqueta);
                    GUI.color = Color.white;
                    GUI.Label(r, e.texto, Hud.etiqueta);
                }
            }
            GUI.color = Color.white;
            float cx = x0 + (Screen.width - x0) * 0.5f;
            if (nivel.tMensaje > 0f && nivel.mensaje.Length > 0)
            {
                float w = Mathf.Min(760f * ui, Screen.width - x0 - 20f), hgt = 40f * ui;
                var caja = new Rect(cx - w / 2f, 46f * ui, w, hgt);
                GUI.color = nivel.mensajeMalo ? new Color(0.75f, 0.15f, 0.1f, 0.92f) : new Color(0.1f, 0.5f, 0.2f, 0.92f);
                GUI.DrawTexture(caja, Texture2D.whiteTexture);
                GUI.color = Color.white;
                var st = new GUIStyle(Hud.titulo) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(15 * ui), wordWrap = true };
                st.normal.textColor = Color.white;
                GUI.Label(caja, nivel.mensaje, st);
            }
            prof.Etiquetas(ui, x0, camM.enabled ? recuadro : new Rect());
            if (embrague) GUI.Box(new Rect(cx - 150, 12, 300, 26), "EMBRAGUE: instrumentos detenidos");
        }

        /// Barra de escala de movimiento, abajo al centro de la vista de los instrumentos
        void BarraEscala(float alto)
        {
            float ancho = Screen.width / ui;
            float x0 = PANEL + 24, w = Mathf.Min(560f, ancho - x0 - 16);
            barraEscala = new Rect(x0 + (ancho - x0 - 16 - w) / 2f, alto - 74, w, 66);
            GUILayout.BeginArea(barraEscala, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Escala  mano : punta   " + Proporcion(escala), titulo, GUILayout.Width(250));
            foreach (float p in ESC_PRESET)
                if (GUILayout.Toggle(Mathf.Abs(escala - Mathf.Round(p * 100f) / 100f) < 0.005f, Proporcion(p), "Button")) PonerEscala(p);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("fino", nota, GUILayout.Width(34));
            // a la izquierda = más fino (10 : 1), a la derecha = más rápido (2 : 1)
            PonerEscala(GUILayout.HorizontalSlider(escala, ESC_MIN, ESC_MAX));
            GUILayout.Label("rápido", nota, GUILayout.Width(46));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void Botones(Dictionary<string, Orbita.Vista> vistas, Orbita orb, bool instrumentos)
        {
            GUILayout.BeginHorizontal();
            int n = 0;
            foreach (var kv in vistas)
            {
                if (n > 0 && n % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                if (GUILayout.Button(kv.Key))
                {
                    var v = kv.Value;
                    if (instrumentos)
                    {
                        seguirMuneca = kv.Key == "Muñeca";
                        if (seguirMuneca) v.mira = ins[act].W;
                    }
                    orb.Ir(v);
                }
                n++;
            }
            GUILayout.EndHorizontal();
        }
    }
}
