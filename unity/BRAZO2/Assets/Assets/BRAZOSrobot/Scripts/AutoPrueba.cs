using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BrazosRobot
{
    /// <summary>
    /// Autoprueba de los niveles (solo para desarrollo). Se activa si existe el archivo "autoprueba.flag"
    /// junto a la carpeta Assets: recorre los seis niveles, guarda una imagen de cada uno en Logs/ y
    /// ejecuta cada ejercicio con un "piloto" que mueve las pinzas por la trayectoria ideal, para
    /// comprobar que la lógica se puede completar y que la geometría no produce errores falsos.
    /// El resultado queda en Logs/autoprueba.txt. Sin ese archivo no hace nada.
    /// </summary>
    public class AutoPrueba : MonoBehaviour
    {
        class Mano { public Vector3 pos, dir = Vector3.right, eje = Vector3.up; public bool cerrada, activa; public Vector3 previa; public float rapidez; }
        readonly Mano[] M = { new Mano(), new Mano() };
        readonly bool[] antes = new bool[2];
        BrazosApp app; string carpeta; readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static string Bandera { get { return System.IO.Path.Combine(Application.dataPath, "..", "autoprueba.flag"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Arrancar()
        {
            if (!System.IO.File.Exists(Bandera)) return;
            new GameObject("AutoPrueba").AddComponent<AutoPrueba>();
        }

        IEnumerator Start()
        {
            carpeta = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            System.IO.Directory.CreateDirectory(carpeta);
            yield return null; yield return null;
#if UNITY_2023_1_OR_NEWER
            app = FindFirstObjectByType<BrazosApp>();
#else
            app = FindObjectOfType<BrazosApp>();
#endif
            if (app == null) { log.AppendLine("No hay BrazosApp"); Fin(); yield break; }
            app.piloto = Piloto;
            for (int i = 0; i < 6; i++)
            {
                M[0].activa = M[1].activa = false; M[0].cerrada = M[1].cerrada = false;
                app.CambiarNivel(i);
                yield return new WaitForSeconds(1.2f);
                Foto("nivel" + (i + 1) + "_inicio");
                if (i == 0) continue;
                log.AppendLine("=== NIVEL " + (i + 1) + ": " + app.NivelActual.titulo);
                IEnumerator guion = i == 1 ? Tejido() : i == 2 ? Bimanual() : i == 3 ? Diseccion() : i == 4 ? Vasos() : Sutura();
                float t0 = Time.time; bool vivo = true;
                while (vivo && Time.time - t0 < 150f && !app.NivelActual.terminado)
                {
                    try { vivo = guion.MoveNext(); }
                    catch (System.Exception e) { log.AppendLine("EXCEPCIÓN en el guion: " + e); vivo = false; }
                    if (vivo) yield return guion.Current;
                }
                yield return null; yield return null;
                var n = app.NivelActual;
                log.AppendLine((n.terminado ? "TERMINADO" : "NO TERMINÓ") + "  puntaje=" + n.puntaje.ToString("0.0") + "  tiempo=" + n.tiempo.ToString("0.0") + "  mensaje=" + n.mensaje);
                foreach (var c in n.criterios) log.AppendLine("   " + c.nombre + " = " + c.valor + (c.perdida > 0.05f ? "   (-" + c.perdida.ToString("0.#") + ")" : ""));
                Foto("nivel" + (i + 1) + "_fin");
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(carpeta, "auto_nivel" + (i + 1) + "_pantalla.png"));
                yield return null; yield return null;
            }
            app.piloto = null;
            app.CambiarNivel(1);
            Fin();
        }

        void Fin()
        {
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(carpeta, "autoprueba.txt"), log.ToString());
                System.IO.File.Delete(Bandera);
            }
            catch (System.Exception) { }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }

        /// Imagen de la cámara de los instrumentos (sin el panel), siempre al mismo tamaño.
        void Foto(string nombre)
        {
            try
            {
                var cam = app.CamaraInstrumentos;
                var rt = new RenderTexture(1280, 720, 24);
                var previo = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = previo;
                var act = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                RenderTexture.active = act;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(carpeta, "auto_" + nombre + ".png"), tex.EncodeToPNG());
                Destroy(rt); Destroy(tex);
            }
            catch (System.Exception e) { log.AppendLine("Foto " + nombre + ": " + e.Message); }
        }

        // ---------------- piloto ----------------
        void Piloto(Herr[] h, float dt)
        {
            for (int m = 0; m < 2; m++)
            {
                var a = M[m]; var t = h[m];
                if (!a.activa) continue;
                t.previa = a.previa; t.pos = a.pos; a.previa = a.pos;
                t.dir = a.dir.normalized; t.eje = a.eje.normalized;
                t.punta = t.pos + t.dir * 7f; t.muneca = t.pos - t.dir * 20f;
                t.trocar = t.pos - t.dir * 150f + new Vector3(0, 60, m == 0 ? 75 : -75);
                t.ap = a.cerrada ? 0f : 60f;
                t.cerrada = a.cerrada; t.cerro = a.cerrada && !antes[m]; t.abrio = !a.cerrada && antes[m];
                antes[m] = a.cerrada;
                float v = (t.pos - t.previa).magnitude / Mathf.Max(dt, 1e-4f);
                a.rapidez = Mathf.Lerp(a.rapidez, v, 1f - Mathf.Exp(-dt / 0.08f)); t.rapidez = a.rapidez;
            }
        }

        void Poner(int m, Vector3 p) { M[m].pos = M[m].previa = p; M[m].activa = true; }
        IEnumerator Ir(int m, Vector3 d, float vel = 40f)
        {
            while ((M[m].pos - d).magnitude > 0.02f) { M[m].pos = Vector3.MoveTowards(M[m].pos, d, vel * Time.deltaTime); yield return null; }
        }
        IEnumerator Pinza(int m, bool cerrar) { M[m].cerrada = cerrar; yield return null; yield return null; yield return null; }
        IEnumerator Espera(float s) { float t0 = Time.time; while (Time.time - t0 < s) yield return null; }
        object Campo(string nombre) { var n = app.NivelActual; return n.GetType().GetField(nombre, BF).GetValue(n); }
        static object Campo(object o, string nombre) { return o.GetType().GetField(nombre, BF).GetValue(o); }
        IEnumerator Sub(IEnumerator e) { while (e.MoveNext()) yield return e.Current; }

        // ---------------- nivel 2 ----------------
        IEnumerator Tejido()
        {
            var n = app.NivelActual; n.Iniciar();
            Vector3 marca = (Vector3)Campo("marca0"), asa = (Vector3)Campo("asa0");
            Poner(1, marca + new Vector3(-20, 5, 0)); Poner(0, asa + new Vector3(-30, 0, 10));
            yield return Sub(Ir(1, marca)); yield return Sub(Pinza(1, true));
            yield return Sub(Ir(1, marca + new Vector3(0.5f, 0.866f, 0f) * 31f, 25f));
            yield return Sub(Espera(3.6f));
            Foto("nivel2_expuesto");
            yield return Sub(Ir(0, asa)); yield return Sub(Pinza(0, true));
            yield return Sub(Ir(0, asa + Vector3.up * 8.8f, 8f));
            yield return Sub(Espera(4.6f));
            yield return Sub(Ir(0, asa + Vector3.up * 2f, 8f)); yield return Sub(Pinza(0, false));
            yield return Sub(Ir(1, marca + new Vector3(0.5f, 0.866f, 0f) * 8f, 25f)); yield return Sub(Pinza(1, false));
            yield return Sub(Espera(0.5f));
        }

        // ---------------- nivel 3 ----------------
        IEnumerator Bimanual()
        {
            var n = app.NivelActual; n.Iniciar();
            var cinta = (Hilo)Campo("cinta");
            const float YT = -5f;
            Poner(1, cinta.p[0] + new Vector3(0, 15, 0)); Poner(0, new Vector3(30, 15, -10));
            M[1].dir = Vector3.right; M[0].dir = new Vector3(0f, -0.2f, 1f);
            yield return Sub(Ir(1, cinta.p[0])); yield return Sub(Pinza(1, true));
            yield return Sub(Ir(1, new Vector3(-24, 0, 30)));
            yield return Sub(Ir(1, new Vector3(-20, YT, 0)));
            yield return Sub(Ir(1, new Vector3(14.5f, YT, 0), 12f));
            Foto("nivel3_tunel");
            yield return Sub(Ir(0, new Vector3(15.5f, YT + 1.5f, 0))); yield return Sub(Pinza(0, true));
            yield return Sub(Pinza(1, false));
            yield return Sub(Ir(1, new Vector3(-30, 5, 10)));
            // centrar: tirar con la derecha
            Vector3 e1 = new Vector3(11.5f, YT, 0);
            Vector3 fuera = new Vector3(0.7f, 0.7f, 0f).normalized;
            yield return Sub(Ir(0, e1 + fuera * 66f, 25f));
            yield return Sub(Espera(0.3f));
            log.AppendLine("   centrado: u=" + cinta.u.ToString("0.0") + " D=" + cinta.TramoOjales.ToString("0.0") + " libreB=" + cinta.LibreB.ToString("0.0"));
            // tomar el otro cabo
            int k = Mathf.Min(cinta.n - 2, Mathf.RoundToInt((cinta.u + cinta.TramoOjales + 22f) / cinta.seg));
            yield return Sub(Ir(1, cinta.p[k])); yield return Sub(Pinza(1, true));
            // las dos manos por encima de las bocas del túnel, para levantar en vertical
            float largoA = (M[0].pos - e1).magnitude;
            yield return Sub(Ir(0, e1 + new Vector3(0.05f, 1f, 0f).normalized * (largoA - 0.5f), 25f));
            yield return Sub(Ir(1, new Vector3(-11.5f, YT, 0) + new Vector3(-0.05f, 1f, 0f).normalized * 18f, 25f));
            // levantar las dos manos hasta la zona verde
            float t0 = Time.time;
            while (Time.time - t0 < 25f)
            {
                float tension = ((Vector3)Campo("d")).magnitude / 16f;
                if (tension >= 0.55f) break;
                M[0].pos += Vector3.up * 5f * Time.deltaTime; M[1].pos += Vector3.up * 5f * Time.deltaTime;
                yield return null;
            }
            Foto("nivel3_suspendido");
            yield return Sub(Espera(5.6f));
            t0 = Time.time;
            while (Time.time - t0 < 3f) { M[0].pos += Vector3.down * 5f * Time.deltaTime; M[1].pos += Vector3.down * 5f * Time.deltaTime; yield return null; }
            yield return Sub(Pinza(0, false)); yield return Sub(Pinza(1, false));
            yield return Sub(Espera(0.5f));
        }

        // ---------------- nivel 4 ----------------
        IEnumerator Diseccion()
        {
            var n = app.NivelActual; n.Iniciar();
            int DM = n.dom, NM = 1 - n.dom;
            Vector3 pl = new Vector3(-54, -3.5f, 0);
            Poner(NM, pl + new Vector3(-15, 10, 0)); Poner(DM, new Vector3(-20, 10, 20));
            M[DM].dir = new Vector3(0.7f, 0.7f, 0f); M[NM].dir = Vector3.right;
            yield return Sub(Ir(NM, pl)); yield return Sub(Pinza(NM, true));
            yield return Sub(Ir(NM, pl + new Vector3(-0.8f, 0.6f, 0f) * 11.5f, 15f));
            Foto("nivel4_traccion");
            var celdas = (IList)Campo("celdas");
            foreach (var c in celdas)
            {
                Vector3 p = (Vector3)Campo(c, "p");
                yield return Sub(Ir(DM, p + Vector3.up * 5f));
                yield return Sub(Ir(DM, p + Vector3.up * 0.4f, 15f));
                yield return Sub(Pinza(DM, true)); yield return Sub(Pinza(DM, false));
                yield return Sub(Ir(DM, p + Vector3.up * 5f));
            }
            yield return Sub(Ir(NM, pl + new Vector3(-0.8f, 0.6f, 0f) * 3f, 15f)); yield return Sub(Pinza(NM, false));
            yield return Sub(Espera(0.5f));
        }

        // ---------------- nivel 5 ----------------
        IEnumerator Vasos()
        {
            var n = app.NivelActual; n.Iniciar();
            int DM = n.dom, NM = 1 - n.dom;
            Vector3 asa = (Vector3)Campo("asa0");
            Poner(NM, asa + new Vector3(-10, 10, 0)); Poner(DM, new Vector3(-20, 5, 40));
            yield return Sub(Ir(NM, asa)); yield return Sub(Pinza(NM, true));
            yield return Sub(Ir(NM, asa + new Vector3(0.5f, 0.866f, 0f) * 14.3f, 10f));
            yield return Sub(Espera(0.4f));
            Foto("nivel5_expuesto");
            var ramas = (IList)Campo("ramas");
            int i = 0;
            foreach (var r in ramas)
            {
                Vector3 v = (Vector3)Campo(r, "v"), P = (Vector3)Campo(r, "P");
                Vector3 eje = (P - v).normalized;
                log.AppendLine("   rama " + (++i) + ": largo expuesto " + (P - v).magnitude.ToString("0.0") + " mm");
                M[DM].dir = Vector3.Cross(eje, Vector3.forward).normalized;
                foreach (float t in new[] { 0.25f, 0.42f, 0.85f, 0.63f })
                {
                    Vector3 q = Vector3.Lerp(v, P, t);
                    yield return Sub(Ir(DM, q + new Vector3(-6, 0, 0)));
                    yield return Sub(Ir(DM, q, 15f));
                    yield return Sub(Pinza(DM, true)); yield return Sub(Pinza(DM, false));
                    yield return Sub(Ir(DM, q + new Vector3(-6, 0, 0)));
                }
            }
            Foto("nivel5_clips");
            yield return Sub(Ir(NM, asa + new Vector3(0.5f, 0.866f, 0f) * 4f, 10f)); yield return Sub(Pinza(NM, false));
            yield return Sub(Espera(0.5f));
        }

        // ---------------- nivel 6 ----------------
        Vector3 cAguja; float delta;      // centro y giro de la aguja mientras la lleva el piloto
        static readonly Vector3 U = new Vector3(0, 0, -1), V = Vector3.up;
        Vector3 EnArco(float grados) { float a = grados * Mathf.Deg2Rad; return U * Mathf.Cos(a) + V * Mathf.Sin(a); }
        void ManoEnAguja(int m, float anguloBase) { Vector3 r = EnArco(anguloBase + delta); M[m].pos = cAguja + r * 6f; M[m].dir = Vector3.right; M[m].eje = r; }
        IEnumerator Aguja(int m, float anguloBase, Vector3 c, float d, float vel = 30f, float velAng = 60f)
        {
            while ((cAguja - c).magnitude > 0.02f || Mathf.Abs(delta - d) > 0.05f)
            {
                cAguja = Vector3.MoveTowards(cAguja, c, vel * Time.deltaTime);
                delta = Mathf.MoveTowards(delta, d, velAng * Time.deltaTime);
                ManoEnAguja(m, anguloBase);
                yield return null;
            }
        }

        IEnumerator Sutura()
        {
            var n = app.NivelActual; n.Iniciar();
            var hilo = (Hilo)Campo("hilo");
            float[] X = { -6.5f, 0.5f, 7.5f };
            const float H0 = 8f;
            for (int s = 0; s < 3; s++)
            {
                yield return Sub(Espera(0.3f));
                Vector3 cola = hilo.p[0];                       // la cola de la aguja nueva
                cAguja = cola - U * 6f; delta = 0f;
                Poner(0, cAguja + new Vector3(-10, 10, 0)); Poner(1, new Vector3(X[s], 25, -25));
                ManoEnAguja(0, 75f); Vector3 toma = M[0].pos;
                M[0].pos = M[0].previa = toma + new Vector3(0, 8, 0);
                yield return Sub(Ir(0, toma)); yield return Sub(Pinza(0, true));
                Vector3 c = new Vector3(X[s], H0 + 2.5f, 0f);
                yield return Sub(Aguja(0, 75f, c + Vector3.up * 4f, 0f));
                yield return Sub(Aguja(0, 75f, c + Vector3.up * 4f, 18f));
                yield return Sub(Aguja(0, 75f, c, 18f, 10f));
                yield return Sub(Aguja(0, 75f, c, 168f, 10f, 50f));               // entra y sale girando sobre su curva
                if (s == 0) Foto("nivel6_aguja");
                // la otra mano recibe la punta
                Vector3 punta = cAguja + EnArco(180f + delta) * 6f;
                yield return Sub(Ir(1, punta + new Vector3(0, 6, -4)));
                M[1].dir = Vector3.right; M[1].eje = EnArco(180f + delta);
                yield return Sub(Ir(1, punta, 15f)); yield return Sub(Pinza(1, true));
                yield return Sub(Pinza(0, false));
                yield return Sub(Ir(0, new Vector3(X[s], 22, 22)));
                yield return Sub(Aguja(1, 180f, c, 346f, 10f, 50f));               // saca la aguja siguiendo la curva
                yield return Sub(Espera(0.2f));
                log.AppendLine("   punto " + (s + 1) + ": tras extraer u=" + hilo.u.ToString("0.0") + " D=" + hilo.TramoOjales.ToString("0.0") + " ojales=" + hilo.ojal.Count);
                // correr el hilo
                Vector3 eOut = hilo.ojal.Count > 0 ? hilo.ojal[0] : new Vector3(X[s], H0, -5.4f);
                Vector3 eIn = hilo.ojal.Count > 1 ? hilo.ojal[1] : new Vector3(X[s], H0, 5.4f);
                Vector3 lejos = new Vector3(0f, 0.6f, -0.8f);
                Vector3 colaAhora = cAguja + EnArco(delta) * 6f;
                Vector3 destinoCola = eOut + lejos * 104f;
                yield return Sub(Aguja(1, 180f, cAguja + (destinoCola - colaAhora), delta, 45f));
                yield return Sub(Espera(0.2f));
                log.AppendLine("   punto " + (s + 1) + ": cabo libre " + hilo.LibreB.ToString("0.0") + " mm");
                // tomar el cabo y ajustar
                int k = Mathf.Min(hilo.n - 2, Mathf.RoundToInt((hilo.u + hilo.TramoOjales + 12f) / hilo.seg));
                float largoB = k * hilo.seg - (hilo.u + hilo.TramoOjales);
                M[0].dir = Vector3.right; M[0].eje = Vector3.up;
                yield return Sub(Ir(0, hilo.p[k] + Vector3.up * 5f)); yield return Sub(Ir(0, hilo.p[k], 15f));
                yield return Sub(Pinza(0, true));
                Vector3 tirar = new Vector3(0f, 0.6f, 0.8f);
                yield return Sub(Ir(0, eIn + tirar * (largoB + 3f), 8f));
                yield return Sub(Espera(2.6f));
                yield return Sub(Pinza(0, false)); yield return Sub(Pinza(1, false));
                if (n.terminado) yield break;
            }
        }
    }
}
