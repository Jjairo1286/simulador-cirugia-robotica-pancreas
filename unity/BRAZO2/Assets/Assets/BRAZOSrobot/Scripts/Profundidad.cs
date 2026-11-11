using System.Collections.Generic;
using UnityEngine;

namespace BrazosRobot
{
    /// <summary>
    /// Ayudas para percibir la profundidad en la imagen del endoscopio (que en el monitor es 2D):
    ///  • Luz del endoscopio: la luz sale de la punta de la cámara y decae con la distancia, como en la
    ///    cirugía real; lo cercano se ve más claro que lo lejano (gradiente de iluminación).
    ///  • Sombras de ayuda: la muñeca y las mordazas proyectan una sombra suave "de luz cenital" sobre el
    ///    tejido. Al acercarse, punta y sombra convergen y la sombra se vuelve más nítida; cuando se tocan,
    ///    hay contacto (sombra virtual: Nicolaou et al. 2006; Mishra et al. 2004).
    ///  • Indicador de distancia: un anillo en la superficie que está detrás de la punta (en la línea de
    ///    visión) que se cierra al acercarse, con la distancia en mm. Es una ayuda de entrenamiento: no
    ///    existe en el quirófano y conviene ir retirándola.
    ///  • 3D anaglifo: dos cámaras separadas como los dos canales del endoscopio estereoscópico del
    ///    da Vinci, combinadas para lentes rojo-cian.
    /// </summary>
    public class Profundidad
    {
        public const int CAPA = 29;                       // capa de los colliders invisibles de la anatomía
        public bool luzEndo, sombras, indicador, estereo;
        public float separacion = 5f;                     // mm entre los dos canales del endoscopio 3D
        /// Distancia (mm) de la punta a la superficie que está detrás de ella en la imagen; NaN si no hay.
        public readonly float[] distancia = { float.NaN, float.NaN };
        public readonly bool[] oculta = new bool[2];

        readonly Camera cam; readonly Transform esp; readonly Instrumento[] ins;
        readonly Light sol, rel; readonly float solI, relI;
        readonly Light puntual;
        // sombras proyectadas: discos suaves sobre el tejido debajo de la muñeca y las mordazas
        const int NS = 20;
        readonly Transform[,] sombra = new Transform[2, NS];
        readonly Material[,] sombraMat = new Material[2, NS];
        readonly Dictionary<MeshCollider, MeshRenderer> cols = new Dictionary<MeshCollider, MeshRenderer>();
        readonly Dictionary<MeshCollider, Bounds> limites = new Dictionary<MeshCollider, Bounds>();
        readonly HashSet<MeshRenderer> vistos = new HashSet<MeshRenderer>();
        Transform raizNivel; float tEscaneo, tRefresco;

        readonly Transform[] marca = new Transform[2];
        readonly MeshFilter[] marcaMf = new MeshFilter[2];
        readonly Material[] marcaMat = new Material[2];
        readonly LineRenderer[] linea = new LineRenderer[2];
        readonly Color[] colMano = { new Color(0.35f, 1f, 0.55f), new Color(0.78f, 0.62f, 1f) };
        readonly Mesh mAnillo, mDisco;

        Camera camDer; Anaglifo anaglifo; RenderTexture rt;

        public Profundidad(Camera cam, Transform esp, Instrumento[] ins, Light sol, Light rel)
        {
            this.cam = cam; this.esp = esp; this.ins = ins; this.sol = sol; this.rel = rel;
            solI = sol.intensity; relI = rel.intensity;
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(CAPA, i, true);
            QualitySettings.pixelLightCount = Mathf.Max(QualitySettings.pixelLightCount, 4);

            puntual = new GameObject("Luz del endoscopio").AddComponent<Light>();
            puntual.type = LightType.Point; puntual.shadows = LightShadows.None;
            puntual.renderMode = LightRenderMode.ForcePixel;
            puntual.color = new Color(1f, 0.97f, 0.93f);

            mAnillo = Corona(0.72f); mDisco = Corona(0f);
            var sh = Shader.Find("Sprites/Default");
            var tex = Difuso(); var quad = Cuadro();
            for (int m = 0; m < 2; m++)
                for (int i = 0; i < NS; i++)
                {
                    var g = new GameObject("Sombra " + m + "." + i);
                    g.AddComponent<MeshFilter>().sharedMesh = quad;
                    var mr = g.AddComponent<MeshRenderer>();
                    var mt = sh != null ? new Material(sh) : Geo.Mat(Color.black, 0f, 0f);
                    mt.mainTexture = tex; mt.renderQueue = 3050;
                    mr.sharedMaterial = mt; sombraMat[m, i] = mt;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                    sombra[m, i] = g.transform; g.SetActive(false);
                }
            for (int m = 0; m < 2; m++)
            {
                marcaMat[m] = sh != null ? new Material(sh) : Geo.Mat(colMano[m], 0f, 0f);
                marcaMat[m].renderQueue = 3100;
                var g = new GameObject("Indicador de profundidad " + (m == 0 ? "derecha" : "izquierda"));
                marca[m] = g.transform;
                marcaMf[m] = g.AddComponent<MeshFilter>(); marcaMf[m].sharedMesh = mAnillo;
                var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = marcaMat[m];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                var lg = new GameObject("Línea de profundidad");
                lg.transform.SetParent(g.transform.parent, false);
                linea[m] = lg.AddComponent<LineRenderer>();
                linea[m].sharedMaterial = marcaMat[m]; linea[m].positionCount = 2; linea[m].useWorldSpace = true;
                linea[m].widthMultiplier = 0.00025f; linea[m].numCapVertices = 2;
                linea[m].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; linea[m].receiveShadows = false;
                g.SetActive(false); lg.SetActive(false);
            }

            luzEndo = PlayerPrefs.GetInt("prof_luz", 1) == 1;
            sombras = PlayerPrefs.GetInt("prof_sombras", 1) == 1;
            indicador = PlayerPrefs.GetInt("prof_indicador", 1) == 1;
            estereo = PlayerPrefs.GetInt("prof_3d", 0) == 1;
            separacion = PlayerPrefs.GetFloat("prof_sep", 5f);
        }

        public void Guardar()
        {
            PlayerPrefs.SetInt("prof_luz", luzEndo ? 1 : 0);
            PlayerPrefs.SetInt("prof_sombras", sombras ? 1 : 0);
            PlayerPrefs.SetInt("prof_indicador", indicador ? 1 : 0);
            PlayerPrefs.SetInt("prof_3d", estereo ? 1 : 0);
            PlayerPrefs.SetFloat("prof_sep", separacion);
        }

        public string Resumen()
        {
            var l = new List<string>();
            if (luzEndo) l.Add("luz del endoscopio");
            if (sombras) l.Add("sombras");
            if (indicador) l.Add("indicador de distancia");
            if (estereo) l.Add("3D anaglifo");
            return l.Count == 0 ? "ninguna" : string.Join(", ", l.ToArray());
        }

        /// Al cambiar de nivel: olvida los colliders del nivel anterior (se destruyeron con él).
        public void NuevoNivel(Transform raiz)
        {
            raizNivel = raiz; cols.Clear(); limites.Clear(); vistos.Clear(); tEscaneo = 0f;
        }

        // ===================== cada cuadro (después de mover cámara e instrumentos) =====================
        public void Actualizar(float dt, Vector3 objetivoMm, bool dentroAbdomen)
        {
            Nivel.ayudas = Resumen();
            Vector3 camPos = cam.transform.position;
            Vector3 obj = esp.TransformPoint(objetivoMm);
            float dist = (obj - camPos).magnitude;
            bool cerca = dist < 0.4f;                   // vistas endoscópicas, no la vista del robot completo

            // ---- luces ----
            puntual.enabled = luzEndo && cerca;
            if (puntual.enabled)
            {
                puntual.transform.position = camPos;
                puntual.range = dist * 3.5f;
                puntual.intensity = 1.5f;
            }

            // con la luz del endoscopio, la luz general baja para no lavar los colores
            sol.intensity = puntual.enabled ? solI * 0.4f : solI;
            rel.intensity = puntual.enabled ? relI * 0.5f : relI;

            // ---- colliders invisibles de la anatomía (para medir distancias) ----
            if ((indicador || sombras) && raizNivel != null)
            {
                tEscaneo -= dt; tRefresco -= dt;
                if (tEscaneo <= 0f) { Escanear(); tEscaneo = 1f; }
                if (tRefresco <= 0f) { Refrescar(); tRefresco = 0.1f; }
            }
            for (int m = 0; m < 2; m++) { Medir(m, camPos, indicador && cerca); Sombra(m, sombras && cerca); }

            // ---- 3D ----
            Estereo(obj);
        }

        void Escanear()
        {
            foreach (var r in raizNivel.GetComponentsInChildren<MeshRenderer>())
            {
                if (vistos.Contains(r)) continue;
                vistos.Add(r);
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                string n = r.gameObject.name;
                if (n == "Cinta" || n == "Aguja" || n == "Hilo") continue;          // lo que se mueve con las pinzas
                if (r.GetComponentInParent<Rigidbody>() != null) continue;
                bool lazo = false;
                for (var t = r.transform; t != null && t != raizNivel; t = t.parent) if (t.name.StartsWith("Lazo")) { lazo = true; break; }
                if (lazo) continue;
                Vector3 s = r.bounds.size;
                if (Mathf.Max(s.x, Mathf.Max(s.y, s.z)) < 0.004f) continue;       // clips, gotas de sangre
                var g = new GameObject("prof"); g.layer = CAPA;
                g.transform.SetParent(r.transform, false);
                var c = g.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh;
                cols[c] = r; limites[c] = mf.sharedMesh.bounds;
            }
        }

        /// Los órganos que se deforman (tejido traccionado) cambian su malla: se vuelve a cocinar el collider.
        void Refrescar()
        {
            List<MeshCollider> muertos = null;
            foreach (var kv in cols)
            {
                var c = kv.Key;
                if (c == null || kv.Value == null) { (muertos = muertos ?? new List<MeshCollider>()).Add(c); continue; }
                var mf = kv.Value.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds, b0 = limites[c];
                if ((b.center - b0.center).sqrMagnitude > 1e-4f || (b.size - b0.size).sqrMagnitude > 1e-4f || c.sharedMesh != mf.sharedMesh)
                {
                    c.sharedMesh = null; c.sharedMesh = mf.sharedMesh; limites[c] = b;
                }
            }
            if (muertos != null) foreach (var c in muertos) { cols.Remove(c); limites.Remove(c); }
        }

        readonly RaycastHit[] hits = new RaycastHit[24];

        void Medir(int m, Vector3 camPos, bool activo)
        {
            distancia[m] = float.NaN; oculta[m] = false;
            if (!activo) { marca[m].gameObject.SetActive(false); linea[m].gameObject.SetActive(false); return; }
            var I = ins[m];
            Vector3 punta = I.tipG.TransformPoint(new Vector3(I.largo, 0, 0));
            Vector3 dir = punta - camPos; float dp = dir.magnitude; dir /= dp;
            int mask = (1 << CAPA) | 1;                 // anatomía (niveles 2–6) y tablero de práctica (nivel 1)
            int n = Physics.RaycastNonAlloc(camPos, dir, hits, dp + 0.08f, mask, QueryTriggerInteraction.Ignore);
            float mejor = float.MaxValue; int k = -1;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (!Valido(h.collider)) continue;
                if (h.distance < dp - 0.003f) { oculta[m] = true; continue; }
                if (h.distance < mejor) { mejor = h.distance; k = i; }
            }
            if (k < 0) { marca[m].gameObject.SetActive(false); linea[m].gameObject.SetActive(false); return; }
            float d = (mejor - dp) * 1000f;                                   // mm (negativo = metida en el tejido)
            distancia[m] = d;
            var hit = hits[k];
            bool contacto = d < 1.5f;
            float r0 = Mathf.Clamp(1.2f + 0.3f * Mathf.Max(d, 0f), 1.2f, 6f);
            marca[m].gameObject.SetActive(true);
            marca[m].position = hit.point - dir * 0.0003f;
            marca[m].rotation = Quaternion.LookRotation(hit.normal);
            marca[m].localScale = Vector3.one * r0 * 0.001f;
            marcaMf[m].sharedMesh = contacto ? mDisco : mAnillo;
            Color c = contacto ? new Color(1f, 0.4f, 0.35f, 0.9f) : new Color(colMano[m].r, colMano[m].g, colMano[m].b, Mathf.Lerp(0.95f, 0.55f, Mathf.InverseLerp(5f, 30f, d)));
            marcaMat[m].color = c;
            bool verLinea = d > 2f;
            linea[m].gameObject.SetActive(verLinea);
            if (verLinea) { linea[m].SetPosition(0, punta); linea[m].SetPosition(1, hit.point - dir * 0.0003f); }
        }

        /// Sombra "de luz cenital": cada punto de la muñeca y las mordazas se proyecta hacia abajo sobre el
        /// tejido. Cuanto más alto está el instrumento, más grande, difusa y clara es la sombra; al tocar el
        /// tejido la sombra se junta con la punta.
        void Sombra(int m, bool activo)
        {
            if (!activo) { for (int i = 0; i < NS; i++) sombra[m, i].gameObject.SetActive(false); return; }
            var I = ins[m];
            Vector3 punta = I.tipG.TransformPoint(new Vector3(I.largo, 0, 0));
            Vector3 mun = I.pitchG.position;
            Vector3 eje = (mun - esp.TransformPoint(I.T)).normalized;
            Vector3 atras = mun - eje * 0.022f;                         // 22 mm de vástago
            int mask = (1 << CAPA) | 1;
            for (int i = 0; i < NS; i++)
            {
                float u = i / (NS - 1f);
                Vector3 p = u < 0.34f ? Vector3.Lerp(atras, mun, u / 0.34f) : Vector3.Lerp(mun, punta, (u - 0.34f) / 0.66f);
                float radioPieza = u < 0.34f ? 4f : Mathf.Lerp(2.6f, 1.2f, (u - 0.34f) / 0.66f);   // mm
                RaycastHit h;
                if (!Abajo(p, mask, out h)) { sombra[m, i].gameObject.SetActive(false); continue; }
                float alto = h.distance * 1000f;
                float r = 1.8f * radioPieza + 0.25f * alto;          // borde difuso: más ancho cuanto más alto
                float a = Mathf.Clamp01(1f - alto / 60f) * (u < 0.34f ? 0.22f : 0.34f);  // los discos se superponen
                if (a < 0.02f) { sombra[m, i].gameObject.SetActive(false); continue; }
                var t = sombra[m, i];
                t.gameObject.SetActive(true);
                t.position = h.point + h.normal * 0.00025f;
                t.rotation = Quaternion.LookRotation(h.normal);
                t.localScale = Vector3.one * (2f * r * 0.001f);
                sombraMat[m, i].color = new Color(0.05f, 0.02f, 0.02f, a);
            }
        }

        bool Abajo(Vector3 p, int mask, out RaycastHit mejor)
        {
            mejor = default(RaycastHit);
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.002f, Vector3.down, hits, 0.06f, mask, QueryTriggerInteraction.Ignore);
            float dmin = float.MaxValue; bool ok = false;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (!Valido(h.collider)) continue;
                if (h.distance < dmin) { dmin = h.distance; mejor = h; ok = true; }
            }
            if (ok) mejor.distance = Mathf.Max(0f, mejor.distance - 0.002f);
            return ok;
        }

        bool Valido(Collider c)
        {
            if (c.attachedRigidbody != null) return false;                 // instrumentos y piezas sueltas
            if (c.gameObject.layer != CAPA) return true;
            MeshRenderer r;
            if (!cols.TryGetValue((MeshCollider)c, out r) || r == null || !r.enabled) return false;
            var mt = r.sharedMaterial;
            return mt == null || mt.renderQueue < 2900;                       // órgano vuelto translúcido
        }

        static Texture2D Difuso()
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        static Mesh Cuadro()
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        /// Distancias en mm junto a cada punta (se llama desde OnGUI con la matriz identidad).
        public void Etiquetas(float ui, float xMin, Rect evitar)
        {
            if (!indicador) return;
            var st = new GUIStyle(Hud.etiqueta) { fontSize = Mathf.RoundToInt(12 * ui), fontStyle = FontStyle.Bold };
            for (int m = 0; m < 2; m++)
            {
                if (float.IsNaN(distancia[m]) && !oculta[m]) continue;
                var I = ins[m];
                Vector3 sp = cam.WorldToScreenPoint(I.tipG.TransformPoint(new Vector3(I.largo, 0, 0)));
                if (sp.z <= 0f || sp.x < xMin || evitar.Contains(new Vector2(sp.x, sp.y))) continue;
                string t = float.IsNaN(distancia[m]) ? "oculta" : (distancia[m] < 1.5f ? "contacto" : distancia[m].ToString("0") + " mm");
                var r = new Rect(sp.x + 10 * ui, Screen.height - sp.y - 22 * ui, 120 * ui, 20 * ui);
                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), t, st);
                GUI.color = distancia[m] < 1.5f ? new Color(1f, 0.5f, 0.45f) : colMano[m];
                GUI.Label(r, t, st);
            }
            GUI.color = Color.white;
        }

        // ===================== 3D anaglifo =====================
        void Estereo(Vector3 obj)
        {
            if (!estereo)
            {
                if (anaglifo != null) anaglifo.enabled = false;
                return;
            }
            if (camDer == null)
            {
                camDer = new GameObject("Cámara ojo derecho").AddComponent<Camera>();
                camDer.enabled = false;
                anaglifo = cam.gameObject.AddComponent<Anaglifo>();
                if (!anaglifo.Listo()) { estereo = false; Object.Destroy(anaglifo); anaglifo = null; Object.Destroy(camDer.gameObject); camDer = null; return; }
            }
            int w = Mathf.Max(16, cam.pixelWidth), h = Mathf.Max(16, cam.pixelHeight);
            if (rt == null || rt.width != w || rt.height != h)
            {
                if (rt != null) rt.Release();
                rt = new RenderTexture(w, h, 24) { antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing) };
            }
            camDer.CopyFrom(cam);
            camDer.enabled = false; camDer.targetTexture = rt; camDer.rect = new Rect(0, 0, 1, 1);
            Vector3 der = cam.transform.right * (separacion * 0.0005f), up = cam.transform.up;
            Vector3 centro = cam.transform.position;
            // convergencia en el punto que mira la cámara (ahí la disparidad es cero, como la pantalla del visor)
            cam.transform.position = centro - der; cam.transform.LookAt(obj, up);
            camDer.transform.position = centro + der; camDer.transform.LookAt(obj, up);
            camDer.Render();
            anaglifo.derecha = rt; anaglifo.enabled = true;
        }

        static Mesh Corona(float interior)
        {
            const int N = 40;
            var v = new List<Vector3>(); var t = new List<int>();
            if (interior <= 0f)
            {
                v.Add(Vector3.zero);
                for (int i = 0; i < N; i++) { float a = i * Mathf.PI * 2f / N; v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0)); }
                for (int i = 0; i < N; i++) { t.Add(0); t.Add(1 + (i + 1) % N); t.Add(1 + i); }
            }
            else
            {
                for (int i = 0; i < N; i++)
                {
                    float a = i * Mathf.PI * 2f / N; var u = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    v.Add(u * interior); v.Add(u);
                }
                for (int i = 0; i < N; i++)
                {
                    int a0 = 2 * i, a1 = 2 * i + 1, b0 = 2 * ((i + 1) % N), b1 = b0 + 1;
                    t.Add(a0); t.Add(b1); t.Add(a1); t.Add(a0); t.Add(b0); t.Add(b1);
                }
            }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }
    }

    /// Combina la imagen de la cámara (ojo izquierdo) con la del ojo derecho en un anaglifo rojo-cian.
    public class Anaglifo : MonoBehaviour
    {
        public RenderTexture derecha;
        Material mat;

        public bool Listo()
        {
            if (mat != null) return true;
            var sh = Resources.Load<Shader>("BrazosAnaglifo");
            if (sh == null) sh = Shader.Find("Hidden/BrazosAnaglifo");
            if (sh == null || !sh.isSupported) return false;
            mat = new Material(sh);
            return true;
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null || derecha == null) { Graphics.Blit(src, dst); return; }
            mat.SetTexture("_Der", derecha);
            Graphics.Blit(src, dst, mat);
        }
    }
}
