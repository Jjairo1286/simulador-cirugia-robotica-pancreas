using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor.Media;
#endif

namespace BrazosRobot
{
    /// <summary>
    /// Videos de demostración: resuelve cada nivel de forma ideal moviendo los instrumentos de verdad
    /// (la misma cadena esclava que usa el mando: filtro, RCM e IK de la muñeca) y graba la pantalla
    /// completa —escena y panel izquierdo— en un MP4 por nivel, dentro de la carpeta Videos del proyecto.
    /// Se activa con el archivo "videos.flag" junto a Assets (menú BRAZOSrobot > Grabar videos). Su
    /// contenido puede limitar los niveles, p. ej. "1 3 6"; vacío = todos. Solo graba dentro del editor.
    /// </summary>
    public class Demostracion : MonoBehaviour
    {
        class Mano { public Vector3 pos, dir = Vector3.right, eje = Vector3.up; public bool fija, cerrada; }
        readonly Mano[] M = { new Mano(), new Mano() };
        BrazosApp app; string carpeta; readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        bool grabando;
#if UNITY_EDITOR
        MediaEncoder enc; Texture2D lienzo;
#endif
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly string[] NOMBRE = { "1_Anillos", "2_Tejido", "3_Bimanual", "4_Diseccion", "5_Vasos", "6_Sutura" };

        public static string Bandera { get { return System.IO.Path.Combine(Application.dataPath, "..", "videos.flag"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Arrancar()
        {
            if (!Application.isEditor || !System.IO.File.Exists(Bandera)) return;
            try
            {
                if (System.IO.File.ReadAllText(Bandera).Trim() == "no") { System.IO.File.Delete(Bandera); return; }   // grabación cancelada
            }
            catch (System.Exception) { return; }
            new GameObject("Demostracion").AddComponent<Demostracion>();
        }

        IEnumerator Start()
        {
            carpeta = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Videos"));
            System.IO.Directory.CreateDirectory(carpeta);
            var lista = new List<int>();
            try { foreach (char c in System.IO.File.ReadAllText(Bandera)) if (c >= '1' && c <= '6') lista.Add(c - '1'); } catch (System.Exception) { }
            if (lista.Count == 0) for (int i = 0; i < 6; i++) lista.Add(i);
            yield return null; yield return null;
#if UNITY_2023_1_OR_NEWER
            app = FindFirstObjectByType<BrazosApp>();
#else
            app = FindObjectOfType<BrazosApp>();
#endif
            if (app == null) { log.AppendLine("No hay BrazosApp"); Fin(); yield break; }
            while (app.NivelActual == null) yield return null;   // que el simulador haya arrancado
            yield return null;
            app.verRecuadroMandos = false;
            app.mandoExterno = Mandar;
            Time.captureFramerate = 30;
            StartCoroutine(Grabador());

            foreach (int i in lista)
            {
                app.CambiarNivel(i);
                yield return null; yield return null;
                var hh = app.HerramientasActuales;
                for (int m = 0; m < 2; m++) { M[m].pos = hh[m].pos; M[m].fija = false; M[m].cerrada = false; }
                yield return Sub(Espera(0.6f));
                Empezar(System.IO.Path.Combine(carpeta, "Nivel_" + NOMBRE[i] + ".mp4"));
                yield return Sub(Espera(2.5f));                    // se lee el objetivo en el panel
                app.NivelActual.Iniciar();
                IEnumerator guion = i == 0 ? Anillos() : i == 1 ? Tejido() : i == 2 ? Bimanual() : i == 3 ? Diseccion() : i == 4 ? Vasos() : Sutura();
                float t0 = Time.time; bool vivo = true;
                while (vivo && Time.time - t0 < 300f && !app.NivelActual.terminado)
                {
                    try { vivo = guion.MoveNext(); }
                    catch (System.Exception e) { log.AppendLine("EXCEPCIÓN nivel " + (i + 1) + ": " + e); vivo = false; }
                    if (vivo) yield return guion.Current;
                }
                float tf = Time.time; while (!app.NivelActual.terminado && Time.time - tf < 3f) yield return null;
                yield return Sub(Espera(5f));                      // puntaje y retroalimentación
                Terminar();
                var n = app.NivelActual;
                log.AppendLine("Nivel " + (i + 1) + ": " + (n.terminado ? "TERMINADO" : "NO TERMINÓ") + " · puntaje " + n.puntaje.ToString("0") + " · tiempo " + n.tiempo.ToString("0.0") + " s · " + n.mensaje);
                foreach (var c in n.criterios) if (c.perdida > 0.05f) log.AppendLine("    " + c.nombre + " = " + c.valor + " (-" + c.perdida.ToString("0.#") + ")");
            }
            app.mandoExterno = null; app.verRecuadroMandos = true; Time.captureFramerate = 0;
            Fin();
        }

        void Fin()
        {
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(carpeta, "videos.txt"), log.ToString());
                System.IO.File.Delete(Bandera);
            }
            catch (System.Exception) { }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        // ---------------- grabación ----------------
        void Empezar(string ruta) { grabando = true; archivo = ruta; }
        string archivo;

        void Terminar()
        {
            grabando = false;
#if UNITY_EDITOR
            if (enc != null) { enc.Dispose(); enc = null; log.AppendLine("Video: " + archivo); }
#endif
        }

        IEnumerator Grabador()
        {
            var fin = new WaitForEndOfFrame();
            while (true)
            {
                yield return fin;
                if (!grabando) continue;
#if UNITY_EDITOR
                var cap = ScreenCapture.CaptureScreenshotAsTexture();
                int w = cap.width & ~1, h = cap.height & ~1;
                if (enc == null)
                {
                    var a = new VideoTrackAttributes
                    {
                        frameRate = new MediaRational(30), width = (uint)w, height = (uint)h,
                        includeAlpha = false, bitRateMode = UnityEditor.VideoBitrateMode.High
                    };
                    if (System.IO.File.Exists(archivo)) System.IO.File.Delete(archivo);
                    enc = new MediaEncoder(archivo, a);
                    lienzo = new Texture2D(w, h, TextureFormat.RGBA32, false);
                }
                if (lienzo.width == w && lienzo.height == h)
                {
                    lienzo.SetPixels(cap.GetPixels(0, 0, w, h)); lienzo.Apply();
                    enc.AddFrame(lienzo);
                }
                Destroy(cap);
#endif
            }
        }

        // ---------------- mando de los instrumentos ----------------
        void Mandar(float dt)
        {
            var hh = app.HerramientasActuales;
            for (int m = 0; m < 2; m++)
            {
                var a = M[m];
                Vector3 dir = a.fija ? a.dir : Plano(a.pos - hh[m].trocar);
                Vector3 eje = a.fija ? a.eje : Vector3.up;
                app.ComandarPunta(m, a.pos, dir, eje, a.cerrada, dt);
            }
        }

        /// Dirección natural del instrumento pero con las mordazas más horizontales: la punta no se
        /// clava hacia los vasos que quedan debajo.
        static Vector3 Plano(Vector3 d) { d.Normalize(); d.y *= 0.35f; return d.normalized; }

        IEnumerator Ir(int m, Vector3 d, float vel = 35f)
        {
            while ((M[m].pos - d).magnitude > 0.02f) { M[m].pos = Vector3.MoveTowards(M[m].pos, d, vel * Time.deltaTime); yield return null; }
            float t0 = Time.time;
            while ((app.HerramientasActuales[m].pos - d).magnitude > 0.5f && Time.time - t0 < 1.2f) yield return null;
        }
        IEnumerator Ambas(Vector3 d0, Vector3 d1, float vel = 30f)
        {
            while ((M[0].pos - d0).magnitude > 0.02f || (M[1].pos - d1).magnitude > 0.02f)
            {
                M[0].pos = Vector3.MoveTowards(M[0].pos, d0, vel * Time.deltaTime);
                M[1].pos = Vector3.MoveTowards(M[1].pos, d1, vel * Time.deltaTime);
                yield return null;
            }
        }
        IEnumerator Pinza(int m, bool cerrar) { M[m].cerrada = cerrar; float t0 = Time.time; while (Time.time - t0 < 0.4f) yield return null; }
        IEnumerator Espera(float s) { float t0 = Time.time; while (Time.time - t0 < s) yield return null; }
        static IEnumerator Sub(IEnumerator e) { while (e.MoveNext()) yield return e.Current; }
        object Campo(string nombre) { var n = app.NivelActual; return n.GetType().GetField(nombre, BF).GetValue(n); }
        static object Campo(object o, string nombre) { return o.GetType().GetField(nombre, BF).GetValue(o); }
        void Fijar(int m, Vector3 dir, Vector3 eje) { M[m].fija = true; M[m].dir = dir.normalized; M[m].eje = eje.normalized; }
        void Libre(int m) { M[m].fija = false; }
        Vector3 Natural(int m) { return Plano(M[m].pos - app.HerramientasActuales[m].trocar); }

        // ---------------- nivel 1: anillos ----------------
        IEnumerator Anillos()
        {
            var pr = app.PracticaAnillos; var raiz = app.NivelActual.raiz;
            var anillos = pr.piezas.FindAll(p => p.esAnillo);
            float[] xs = { -5f, 15f, 35f }, zs = { 24f, 38f };
            Vector3 abajo = Vector3.down, tang = Vector3.forward;
            const float ALTO = 6f;
            for (int i = 0; i < anillos.Count; i++)
            {
                var p = anillos[i];
                Vector3 c = raiz.InverseTransformPoint(p.transform.position);
                Vector3 g = c + new Vector3(-6.5f, 2f, 0f);
                Fijar(1, abajo, tang);
                yield return Sub(Ir(1, new Vector3(g.x, ALTO + 4f, g.z), 60f));
                yield return Sub(Ir(1, g, 30f));
                yield return Sub(Pinza(1, true));
                yield return Sub(Ir(1, new Vector3(g.x, ALTO, g.z), 30f));
                Vector3 centroT = new Vector3(15f, ALTO - 2f, 0f);
                yield return Sub(Ir(1, centroT + new Vector3(-6.5f, 2f, 0f), 50f));
                yield return Sub(Espera(0.2f));
                // la derecha toma el anillo por el lado opuesto: transferencia en el aire
                c = raiz.InverseTransformPoint(p.transform.position);
                Vector3 gr = c + new Vector3(6.5f, 2f, 0f);
                Fijar(0, abajo, tang);
                yield return Sub(Ir(0, gr + Vector3.up * 8f, 60f));
                yield return Sub(Ir(0, gr, 25f));
                yield return Sub(Pinza(0, true));
                yield return Sub(Pinza(1, false));
                yield return Sub(Ir(1, new Vector3(-5f, 12f, -45f), 60f));
                // lleva el anillo sobre su poste de destino y lo suelta
                c = raiz.InverseTransformPoint(p.transform.position);
                Vector3 off = c - M[0].pos;
                Vector3 dest = new Vector3(xs[i / 2], 0f, zs[i % 2]);
                yield return Sub(Ir(0, new Vector3(dest.x, ALTO - 2f, dest.z) - new Vector3(off.x, -2f, off.z) + Vector3.up * 0f, 50f));
                yield return Sub(Ir(0, new Vector3(dest.x - off.x, -1f - off.y, dest.z - off.z), 20f));
                yield return Sub(Pinza(0, false));
                yield return Sub(Espera(0.5f));
                yield return Sub(Ir(0, M[0].pos + Vector3.up * 10f, 50f));
            }
            Libre(0); Libre(1);
            yield return Sub(Espera(1f));
        }

        // ---------------- nivel 2: tejido ----------------
        IEnumerator Tejido()
        {
            Vector3 marca = (Vector3)Campo("marca0"), asa = (Vector3)Campo("asa0");
            Vector3 arriba = new Vector3(0.5f, 0.866f, 0f);
            yield return Sub(Ir(1, marca + new Vector3(-12, 8, 0)));
            yield return Sub(Ir(1, marca, 20f)); yield return Sub(Pinza(1, true));
            yield return Sub(Ir(1, marca + arriba * 31f, 12f));
            yield return Sub(Espera(3.6f));
            yield return Sub(Ir(0, asa + new Vector3(-12, 4, 6)));
            yield return Sub(Ir(0, asa, 15f)); yield return Sub(Pinza(0, true));
            yield return Sub(Ir(0, asa + Vector3.up * 8.8f, 5f));
            yield return Sub(Espera(4.6f));
            yield return Sub(Ir(0, asa + Vector3.up * 2f, 5f)); yield return Sub(Pinza(0, false));
            yield return Sub(Ir(0, asa + new Vector3(-12, 10, 8)));
            yield return Sub(Ir(1, marca + arriba * 8f, 12f)); yield return Sub(Pinza(1, false));
            yield return Sub(Ir(1, marca + new Vector3(-12, 10, 0)));
        }

        // ---------------- nivel 3: bimanual ----------------
        IEnumerator Bimanual()
        {
            var cinta = (Hilo)Campo("cinta");
            const float YT = -5f;
            Vector3 e1 = new Vector3(11.5f, YT, 0), e2 = new Vector3(-11.5f, YT, 0);
            yield return Sub(Ir(1, cinta.p[0] + new Vector3(0, 10, 0)));
            yield return Sub(Ir(1, cinta.p[0], 15f)); yield return Sub(Pinza(1, true));
            yield return Sub(Ir(1, new Vector3(-26, 2, 18), 30f));
            Fijar(1, new Vector3(1f, -0.05f, 0.12f), Vector3.up);
            yield return Sub(Ir(1, new Vector3(-22, YT, 0), 20f));
            yield return Sub(Ir(1, new Vector3(14.5f, YT, 0), 8f));        // por el túnel, despacio
            Fijar(0, new Vector3(-0.15f, 0.05f, -1f), Vector3.up);          // recibe de lado, sin apuntar a la porta
            yield return Sub(Ir(0, new Vector3(16f, YT + 6f, 1f)));
            yield return Sub(Ir(0, new Vector3(16f, YT + 1.5f, 0f), 10f));
            yield return Sub(Pinza(0, true));
            yield return Sub(Pinza(1, false));
            yield return Sub(Ir(1, new Vector3(-26, YT, 0), 15f));
            yield return Sub(Ir(0, new Vector3(18f, YT + 8f, 0f), 15f));
            Libre(0);
            Libre(1);
            yield return Sub(Ir(1, new Vector3(-30, 8, 10)));
            // centrar la cinta
            Vector3 fuera = new Vector3(0.7f, 0.7f, 0f).normalized;
            yield return Sub(Ir(0, e1 + fuera * 66f, 20f));
            yield return Sub(Espera(0.3f));
            float largoA = (M[0].pos - e1).magnitude;
            yield return Sub(Ir(0, e1 + new Vector3(0.05f, 1f, 0f).normalized * (largoA - 0.5f), 20f));
            int k = Mathf.Min(cinta.n - 2, Mathf.RoundToInt((cinta.u + cinta.TramoOjales + 22f) / cinta.seg));
            yield return Sub(Ir(1, cinta.p[k] + Vector3.up * 8f));
            yield return Sub(Ir(1, cinta.p[k], 15f)); yield return Sub(Pinza(1, true));
            yield return Sub(Ir(1, e2 + new Vector3(-0.05f, 1f, 0f).normalized * 18f, 20f));
            float t0 = Time.time;
            while (Time.time - t0 < 25f)
            {
                float tension = ((Vector3)Campo("d")).magnitude / 16f;
                if (tension >= 0.55f) break;
                M[0].pos += Vector3.up * 4f * Time.deltaTime; M[1].pos += Vector3.up * 4f * Time.deltaTime;
                yield return null;
            }
            yield return Sub(Espera(5.6f));
            t0 = Time.time;
            while (Time.time - t0 < 3f) { M[0].pos += Vector3.down * 4f * Time.deltaTime; M[1].pos += Vector3.down * 4f * Time.deltaTime; yield return null; }
            yield return Sub(Pinza(0, false)); yield return Sub(Pinza(1, false));
            yield return Sub(Ambas(M[0].pos + new Vector3(-10, 10, 10), M[1].pos + new Vector3(-10, 10, -10)));
        }

        // ---------------- nivel 4: disección ----------------
        IEnumerator Diseccion()
        {
            var n = app.NivelActual; int DM = n.dom, NM = 1 - n.dom;
            Vector3 pl = new Vector3(-54, -3.5f, 0);
            Vector3 tr = new Vector3(-0.8f, 0.6f, 0f);
            yield return Sub(Ir(NM, pl + new Vector3(-6, 8, 0)));
            yield return Sub(Ir(NM, pl, 15f)); yield return Sub(Pinza(NM, true));
            yield return Sub(Ir(NM, pl + tr * 11.5f, 6f));
            var celdas = (IList)Campo("celdas");
            Fijar(DM, new Vector3(0.92f, 0.3f, DM == 0 ? 0.05f : -0.05f), Vector3.up);     // hojas casi paralelas a la vena, apuntando lejos de Henle
            foreach (var c in celdas)
            {
                Vector3 p = (Vector3)Campo(c, "p");
                yield return Sub(Ir(DM, p + Vector3.up * 5f, 40f));
                yield return Sub(Ir(DM, p + Vector3.up * 0.4f, 15f));
                yield return Sub(Pinza(DM, true)); yield return Sub(Pinza(DM, false));
                yield return Sub(Ir(DM, p + Vector3.up * 5f, 30f));
            }
            yield return Sub(Ir(DM, M[DM].pos + Vector3.up * 12f, 20f));       // sube antes de reorientar la tijera
            Libre(DM);
            yield return Sub(Ir(DM, new Vector3(-20, 14, DM == 0 ? 20 : -20)));
            yield return Sub(Ir(NM, pl + tr * 3f, 6f)); yield return Sub(Pinza(NM, false));
            yield return Sub(Ir(NM, pl + new Vector3(-8, 10, 0)));
        }

        // ---------------- nivel 5: vasos ----------------
        IEnumerator Vasos()
        {
            var n = app.NivelActual; int DM = n.dom, NM = 1 - n.dom;
            Vector3 asa = (Vector3)Campo("asa0");
            Vector3 arriba = new Vector3(0.5f, 0.866f, 0f);
            yield return Sub(Ir(NM, asa + new Vector3(-10, 6, 0)));
            yield return Sub(Ir(NM, asa, 15f)); yield return Sub(Pinza(NM, true));
            yield return Sub(Ir(NM, asa + arriba * 14.3f, 6f));
            yield return Sub(Espera(0.6f));
            var ramas = (IList)Campo("ramas");
            foreach (var r in ramas)
            {
                Vector3 v = (Vector3)Campo(r, "v"), P = (Vector3)Campo(r, "P");
                Vector3 eje = (P - v).normalized;
                Vector3 d = Vector3.Cross(eje, Vector3.forward).normalized;
                Vector3 q0 = Vector3.Lerp(v, P, 0.5f);
                if (Vector3.Dot(d, Natural(DM)) < 0f) d = -d;
                Fijar(DM, d, Vector3.Cross(d, eje));
                foreach (float t in new[] { 0.25f, 0.42f, 0.85f, 0.63f })
                {
                    Vector3 q = Vector3.Lerp(v, P, t);
                    yield return Sub(Ir(DM, q - d * 6f, 35f));
                    yield return Sub(Ir(DM, q, 12f));
                    yield return Sub(Pinza(DM, true)); yield return Sub(Pinza(DM, false));
                    yield return Sub(Ir(DM, q - d * 6f, 25f));
                }
            }
            Libre(DM);
            yield return Sub(Ir(DM, new Vector3(-15, 12, DM == 0 ? 60 : 20)));
            yield return Sub(Ir(NM, asa + arriba * 4f, 6f)); yield return Sub(Pinza(NM, false));
            yield return Sub(Ir(NM, asa + new Vector3(-10, 8, 0)));
        }

        // ---------------- nivel 6: sutura ----------------
        Vector3 cAguja; float delta;
        static readonly Vector3 U = new Vector3(0, 0, -1), V = Vector3.up;
        static Vector3 EnArco(float grados) { float a = grados * Mathf.Deg2Rad; return U * Mathf.Cos(a) + V * Mathf.Sin(a); }
        void ManoEnAguja(int m, float b) { Vector3 r = EnArco(b + delta); M[m].pos = cAguja + r * 6f; Fijar(m, Vector3.right, r); }
        IEnumerator Aguja(int m, float b, Vector3 c, float d, float vel = 20f, float velAng = 45f)
        {
            while ((cAguja - c).magnitude > 0.02f || Mathf.Abs(delta - d) > 0.05f)
            {
                cAguja = Vector3.MoveTowards(cAguja, c, vel * Time.deltaTime);
                delta = Mathf.MoveTowards(delta, d, velAng * Time.deltaTime);
                ManoEnAguja(m, b);
                yield return null;
            }
            yield return Sub(Espera(0.15f));
        }

        IEnumerator Sutura()
        {
            var hilo = (Hilo)Campo("hilo");
            float[] X = { -6.5f, 0.5f, 7.5f };
            const float H0 = 8f;
            for (int s = 0; s < 3; s++)
            {
                yield return Sub(Espera(0.4f));
                Vector3 cola = hilo.p[0];
                cAguja = cola - U * 6f; delta = 0f;
                Vector3 toma = cAguja + EnArco(75f) * 6f;
                Libre(0);
                yield return Sub(Ir(0, toma + new Vector3(-4, 8, 0)));
                Fijar(0, Vector3.right, EnArco(75f));
                yield return Sub(Ir(0, toma, 12f)); yield return Sub(Pinza(0, true));
                Vector3 c = new Vector3(X[s], H0 + 2.5f, 0f);
                yield return Sub(Aguja(0, 75f, c + Vector3.up * 4f, 0f));
                yield return Sub(Aguja(0, 75f, c + Vector3.up * 4f, 18f));
                yield return Sub(Aguja(0, 75f, c, 18f, 8f));
                yield return Sub(Aguja(0, 75f, c, 168f, 8f, 35f));           // entra y sale girando sobre su curva
                Vector3 punta = cAguja + EnArco(180f + delta) * 6f;
                Libre(1);
                yield return Sub(Ir(1, punta + new Vector3(-3, 6, -5)));
                Fijar(1, Vector3.right, EnArco(180f + delta));
                yield return Sub(Ir(1, punta, 10f)); yield return Sub(Pinza(1, true));
                yield return Sub(Pinza(0, false));
                Libre(0);
                yield return Sub(Ir(0, new Vector3(X[s] - 6, 22, 20)));
                yield return Sub(Aguja(1, 180f, c, 346f, 8f, 35f));          // la otra mano la saca siguiendo la curva
                Vector3 eOut = hilo.ojal.Count > 0 ? hilo.ojal[0] : new Vector3(X[s], H0, -5.4f);
                Vector3 eIn = hilo.ojal.Count > 1 ? hilo.ojal[1] : new Vector3(X[s], H0, 5.4f);
                Vector3 colaAhora = cAguja + EnArco(delta) * 6f;
                Vector3 destino = eOut + new Vector3(0f, 0.6f, -0.8f) * 104f;
                yield return Sub(Aguja(1, 180f, cAguja + (destino - colaAhora), delta, 30f));
                int k = Mathf.Min(hilo.n - 2, Mathf.RoundToInt((hilo.u + hilo.TramoOjales + 12f) / hilo.seg));
                float largoB = k * hilo.seg - (hilo.u + hilo.TramoOjales);
                yield return Sub(Ir(0, hilo.p[k] + Vector3.up * 6f));
                yield return Sub(Ir(0, hilo.p[k], 12f)); yield return Sub(Pinza(0, true));
                yield return Sub(Ir(0, eIn + new Vector3(0f, 0.6f, 0.8f) * (largoB + 3f), 6f));
                yield return Sub(Espera(2.6f));
                yield return Sub(Pinza(0, false)); yield return Sub(Pinza(1, false));
                Libre(1);
                yield return Sub(Ir(1, new Vector3(X[s] - 6, 22, -22)));
                if (app.NivelActual.terminado) yield break;
            }
        }
    }
}
