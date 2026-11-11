using System.Collections.Generic;
using UnityEngine;

namespace BrazosRobot
{
    /// <summary>
    /// NIVEL 6 — Sutura: puntos de la pancreatoyeyunostomía.
    ///
    /// Campo tras la resección: muñón del páncreas (con el conducto de Wirsung a la vista) y asa de yeyuno
    /// adosada. Hay que dar 3 puntos separados entre la cápsula pancreática y la seromuscular del yeyuno.
    /// Cada punto: tomar bien la aguja, entrar perpendicular en la marca azul, girar la muñeca siguiendo
    /// la curvatura de la aguja, salir en la marca blanca, extraer la aguja con la OTRA mano, correr el
    /// hilo hasta dejar un cabo corto y ajustar con una mano en cada lado sin cortar el tejido (el
    /// páncreas es friable: el exceso de tensión lo corta como un hilo corta el queso).
    /// El anudado está simplificado: se evalúa la tensión simétrica del ajuste, no el nudo en sí.
    /// </summary>
    public class NivelSutura : Nivel
    {
        /// Aguja curva de 1/2 círculo y 6 mm de radio (≈ 19 mm de longitud).
        class Aguja
        {
            public const int M = 13; public const float R = 6f;
            public readonly Vector3[] q = new Vector3[M + 1];      // 0 = cola (hilo) … M−1 = punta; M = centro
            readonly Vector3[] rel = new Vector3[M + 1];
            public int mano = -1;
            public Vector3 Punta { get { return q[M - 1]; } }
            public Vector3 Cola { get { return q[0]; } }
            public Vector3 Centro { get { return q[M]; } }
            public Vector3 Normal { get { return Vector3.Cross(q[0] - q[M], q[M / 2] - q[M]).normalized; } }
            public Vector3 TanPunta { get { return (q[M - 1] - q[M - 2]).normalized; } }

            public void Colocar(Vector3 c, Vector3 u, Vector3 v)
            {
                for (int i = 0; i < M; i++)
                {
                    float th = i * Mathf.PI / (M - 1);
                    q[i] = c + (u * Mathf.Cos(th) + v * Mathf.Sin(th)) * R;
                }
                q[M] = c; mano = -1;
            }
            public void Tomar(Herr t)
            {
                mano = t.mano; Vector3 lado = t.Lado;
                for (int i = 0; i <= M; i++)
                {
                    Vector3 d = q[i] - t.pos;
                    rel[i] = new Vector3(Vector3.Dot(d, t.dir), Vector3.Dot(d, t.eje), Vector3.Dot(d, lado));
                }
            }
            public void Seguir(Herr t)
            {
                Vector3 lado = t.Lado;
                for (int i = 0; i <= M; i++) q[i] = t.pos + t.dir * rel[i].x + t.eje * rel[i].y + lado * rel[i].z;
            }
            public void Mover(Vector3 d) { for (int i = 0; i <= M; i++) q[i] += d; }
            public float Cercana(Vector3 p, out int idx)
            {
                float best = float.MaxValue; idx = 0;
                for (int i = 0; i < M; i++) { float d = (q[i] - p).sqrMagnitude; if (d < best) { best = d; idx = i; } }
                return Mathf.Sqrt(best);
            }
            /// Distancia de un orificio al círculo de la aguja: 0 si la aguja gira sobre su propia curva.
            public float AlCirculo(Vector3 e)
            {
                Vector3 n = Normal, d = e - Centro; float hh = Vector3.Dot(d, n);
                float r = (d - n * hh).magnitude - R;
                return Mathf.Sqrt(hh * hh + r * r);
            }
        }

        struct Punto { public float errIn, errOut, angIn, desv, cabo, tMax, prof; public bool otraMano; }
        enum Est { Fuera, Mal, Dentro, Salio, Hilo }

        const float H0 = 8f, Z_IN = 5.2f, Z_OUT = -5.2f, ERR_MAX = 6.5f, DESV_MAX = 3.5f;
        const float T_MIN = 0.30f, T_MAX = 0.90f, T_SOSTEN = 1.5f, T_MM = 7f, CABO_MIN = 15f, CABO_MAX = 65f;
        static readonly float[] X_P = { -6.5f, 0.5f, 7.5f };
        static readonly Vector3 SPAWN = new Vector3(2f, 0f, 26f);

        Aguja aguja; Hilo hilo; TuboDin visAguja, visHilo;
        readonly int[] agarreHilo = { -1, -1 };
        readonly Transform[] mIn = new Transform[3], mOut = new Transform[3];
        Material mMarca, mMarcaAct, mMarcaSal, mHecho;
        Est est; int idx;
        Vector3 eIn, eOut, puntaPrev; bool puntaBajoPrev, colaEntro, desgarroPunto, agarreEvaluado;
        int manoEntrada; float fracAgarre, perpAgarre;
        Punto act; readonly List<Punto> hechos = new List<Punto>();
        float sosten, caidaAcum, desvAhora, tension;
        int fallidas, retiros, desgarros, caidas, hiloPerdido, cortesNudo, agarresMalos, reposiciones, pinzamientos;

        public NivelSutura()
        {
            numero = 6; corto = "Sutura"; titulo = "Sutura: puntos de la pancreatoyeyunostomía";
            intro = "Tres puntos entre el muñón del páncreas (derecha) y el yeyuno (izquierda). En cada punto: 1) toma la aguja a la mitad o dos tercios desde la punta, perpendicular a la pinza; 2) entra en la marca AZUL con la punta hacia abajo; 3) gira la muñeca para que la aguja siga su curva y salga en la marca BLANCA; 4) saca la aguja con la otra mano; 5) corre el hilo hasta dejar un cabo corto; 6) toma el cabo con una mano y el hilo o la aguja con la otra, y ajusta en la zona verde.";
            tiempoPar = 300f; recorridoPar = 280f;
            Centrar(new Vector3(0, 8, 5), 100f, 38f);
        }

        float Piso(float x, float z) { return Mathf.Max(anat.Suelo(x, z), anat.pancreas.Techo(x, z)); }
        static bool Bajo(Vector3 p) { return p.y < H0 - 0.25f && Mathf.Abs(p.z) <= 10f && Mathf.Abs(p.x) <= 15f; }

        protected override void Construir()
        {
            rol[DM] = "da el punto"; rol[NM] = "recibe la aguja y sujeta el cabo";
            herramienta[DM] = TipoInstrumento.Portaagujas; herramienta[NM] = TipoInstrumento.Portaagujas;
            anat = new Anatomia(raiz, true);
            aguja = new Aguja();
            hilo = new Hilo(61, 2.5f) { radio = 0.35f, suelo = Piso };
            visAguja = new TuboDin(raiz, "Aguja", Geo.Mat(0xeef2f5, 0.9f, 0.85f), 0.6f, 8);
            visHilo = new TuboDin(raiz, "Hilo", Geo.Mat(0x5a1fa8, 0f, 0.5f), 0.45f, 5);
            mMarca = Geo.Mat(0x55606a, 0f, 0.3f); mMarcaAct = Geo.Mat(0x1f6fff, 0f, 0.6f); mMarcaSal = Geo.Mat(0xffffff, 0f, 0.6f);
            mHecho = Geo.Mat(0x6a2fa0, 0f, 0.5f);
            for (int i = 0; i < 3; i++)
            {
                float yi = anat.pancreas.Techo(X_P[i], Z_IN), yo = anat.yeyuno.Techo(X_P[i], Z_OUT);
                mIn[i] = Marca(new Vector3(X_P[i], yi + 0.1f, Z_IN));
                mOut[i] = Marca(new Vector3(X_P[i], yo + 0.1f, Z_OUT));
            }
            anat.etiquetas.Add(new Etiqueta("Entrada (cápsula pancreática)", new Vector3(X_P[2] + 3f, H0 + 2f, Z_IN)));
            anat.etiquetas.Add(new Etiqueta("Salida (seromuscular)", new Vector3(X_P[2] + 3f, H0 + 2f, Z_OUT)));
            Reponer();
        }

        Transform Marca(Vector3 p)
        {
            var g = Geo.Sphere(1f, mMarca, raiz, p); g.name = "Marca";
            g.transform.localScale = new Vector3(2f, 0.7f, 2f);
            return g.transform;
        }

        /// Aguja nueva, de pie sobre el cuerpo del páncreas (punta a la derecha), con su hilo tendido.
        void Reponer()
        {
            float y = Piso(SPAWN.x, SPAWN.z) + 0.4f;
            aguja.Colocar(new Vector3(SPAWN.x, y, SPAWN.z), new Vector3(0, 0, -1), Vector3.up);
            hilo.QuitarOjales(); hilo.SinAgarres();
            agarreHilo[0] = agarreHilo[1] = -1;
            Vector3 c = aguja.Cola;
            for (int k = 0; k < hilo.n; k++)
            {
                float s = k * hilo.seg; Vector3 p;
                if (s < 12f) p = new Vector3(c.x + s, 0, c.z);
                else if (s < 62f) p = new Vector3(c.x + 12f, 0, c.z + (s - 12f));
                else if (s < 68f) p = new Vector3(c.x + 12f + (s - 62f), 0, c.z + 50f);
                else if (s < 118f) p = new Vector3(c.x + 18f, 0, c.z + 50f - (s - 68f));
                else if (s < 124f) p = new Vector3(c.x + 18f + (s - 118f), 0, c.z);
                else p = new Vector3(c.x + 24f, 0, c.z + (s - 124f));
                p.y = Piso(p.x, p.z) + hilo.radio;
                hilo.p[k] = p;
            }
            hilo.p[0] = c;
            est = Est.Fuera; puntaBajoPrev = false; puntaPrev = aguja.Punta; colaEntro = false; desgarroPunto = false; agarreEvaluado = false;
            act = new Punto(); sosten = 0f; caidaAcum = 0f; desvAhora = 0f; tension = 0f;
        }

        static Vector3 Cruce(Vector3 a, Vector3 b)
        {
            float da = a.y - H0, db = b.y - H0;
            float t = Mathf.Abs(da - db) > 1e-5f ? Mathf.Clamp01(da / (da - db)) : 0.5f;
            Vector3 p = Vector3.Lerp(a, b, t); p.y = H0; return p;
        }

        void Cerro(Herr t)
        {
            int m = t.mano, i; float dh;
            if (aguja.mano != m && aguja.Cercana(t.pos, out i) < 5.5f)
            {
                aguja.Tomar(t); caidaAcum = 0f; agarreHilo[m] = -1;
                fracAgarre = (Aguja.M - 1 - i) / (float)(Aguja.M - 1);
                perpAgarre = Mathf.Abs(Vector3.Dot(t.dir, aguja.Normal));
                return;
            }
            int k = hilo.Cercano(t.pos, out dh);
            if (agarreHilo[m] < 0 && aguja.mano != m && dh < 5.5f && !hilo.EnOjales(k) && k > 0) { agarreHilo[m] = k; return; }
            if (anat.pancreas.Dist(t.pos) < -1.5f) { pinzamientos++; Avisar("No pinces el muñón pancreático: es friable", true); }
        }

        void PuntoHecho()
        {
            act.cabo = hilo.LibreB; hechos.Add(act);
            // deja el punto dibujado
            Vector3 medio = (eIn + eOut) * 0.5f + Vector3.up * 1.4f;
            var t = new Tubo("punto", new[] { eIn - Vector3.up * 0.5f, eIn + Vector3.up * 0.6f, medio, eOut + Vector3.up * 0.6f, eOut - Vector3.up * 0.5f }, 0.38f, 4);
            Geo.Obj(t.Malla(6), mHecho, raiz, "Punto " + (idx + 1));
            Geo.Sphere(0.9f, mHecho, raiz, medio);
            idx++;
            if (idx >= 3) { Terminar(); return; }
            Avisar("Punto " + idx + " ajustado. Toma la aguja nueva para el siguiente", false);
            Reponer();
        }

        protected override void Paso(float dt, Herr[] h)
        {
            // ---- pinzas ----
            foreach (var t in h)
            {
                if (t.cerro) Cerro(t);
                if (t.abrio)
                {
                    if (aguja.mano == t.mano) { aguja.mano = -1; caidaAcum = 0f; }
                    agarreHilo[t.mano] = -1;
                }
            }
            if (aguja.mano >= 0) aguja.Seguir(h[aguja.mano]);

            // ---- aguja y tejido ----
            int nb = 0; float prof = 0f;
            for (int i = 0; i < Aguja.M; i++) if (Bajo(aguja.q[i])) { nb++; prof = Mathf.Max(prof, H0 - aguja.q[i].y); }
            bool pb = Bajo(aguja.Punta);
            desvAhora = 0f;
            switch (est)
            {
                case Est.Fuera:
                    if (pb && !puntaBajoPrev && aguja.mano >= 0)
                    {
                        eIn = Cruce(puntaPrev, aguja.Punta);
                        float err = Mat3.Horizontal(eIn, new Vector3(X_P[idx], 0, Z_IN));
                        if (err > ERR_MAX)
                        {
                            fallidas++; est = Est.Mal;
                            Avisar("Punción a " + err.ToString("0.0") + " mm de la marca azul: retira la aguja y corrige", true);
                        }
                        else
                        {
                            act = new Punto { errIn = err, angIn = Vector3.Angle(aguja.TanPunta, Vector3.down) };
                            manoEntrada = aguja.mano; est = Est.Dentro; colaEntro = false; desgarroPunto = false;
                            if (!agarreEvaluado)
                            {
                                agarreEvaluado = true;
                                if (fracAgarre < 0.25f || fracAgarre > 0.9f) { agarresMalos++; Avisar("Aguja tomada " + (fracAgarre < 0.4f ? "demasiado cerca de la punta" : "demasiado cerca de la cola"), true); }
                                else if (perpAgarre < 0.55f) { agarresMalos++; Avisar("La aguja no está perpendicular a la pinza", true); }
                            }
                        }
                    }
                    break;
                case Est.Mal:
                    if (nb == 0) est = Est.Fuera;
                    break;
                case Est.Dentro:
                    desvAhora = aguja.AlCirculo(eIn);
                    act.prof = Mathf.Max(act.prof, prof);
                    if (!pb && puntaBajoPrev)
                    {
                        Vector3 e = Cruce(puntaPrev, aguja.Punta);
                        if ((e - eIn).magnitude < 2.5f) { est = Est.Fuera; retiros++; }
                        else
                        {
                            eOut = e; act.errOut = Mat3.Horizontal(e, new Vector3(X_P[idx], 0, Z_OUT)); est = Est.Salio;
                            Avisar(act.errOut > ERR_MAX ? "La aguja salió a " + act.errOut.ToString("0.0") + " mm de la marca blanca" : "Punta fuera: tómala con la otra mano y sácala siguiendo la curva", act.errOut > ERR_MAX);
                        }
                    }
                    else if (nb == 0) { est = Est.Fuera; retiros++; }
                    break;
                case Est.Salio:
                    if (Bajo(aguja.Cola)) colaEntro = true;
                    desvAhora = aguja.AlCirculo(eOut);
                    if (!colaEntro) desvAhora = Mathf.Max(desvAhora, aguja.AlCirculo(eIn));
                    act.prof = Mathf.Max(act.prof, prof);
                    if (nb == 0)
                    {
                        if ((aguja.Cola - eOut).magnitude <= (aguja.Cola - eIn).magnitude)
                        {
                            est = Est.Hilo; act.otraMano = aguja.mano >= 0 && aguja.mano != manoEntrada;
                            hilo.PonerOjales(new[] { eOut, eIn }, Mathf.Max(1f, (aguja.Cola - eOut).magnitude));
                            Avisar(act.otraMano ? "Aguja extraída. Corre el hilo hasta dejar un cabo corto" : "Aguja extraída con la misma mano: lo correcto es recibirla con la otra", !act.otraMano);
                        }
                        else { est = Est.Fuera; retiros++; Avisar("Sacaste la aguja hacia atrás: repite el punto", true); }
                    }
                    break;
            }
            if (est == Est.Dentro || est == Est.Salio)
            {
                act.desv = Mathf.Max(act.desv, desvAhora);
                if (desvAhora > DESV_MAX && !desgarroPunto)
                {
                    desgarroPunto = true; desgarros++;
                    Avisar("DESGARRO: empujaste la aguja en vez de girarla sobre su curva", true);
                }
            }
            puntaBajoPrev = pb; puntaPrev = aguja.Punta;

            // ---- aguja suelta: cae hasta apoyarse ----
            bool enTejido = est == Est.Dentro || est == Est.Salio || est == Est.Mal;
            if (aguja.mano < 0 && !enTejido)
            {
                float gap = float.MaxValue;
                for (int i = 0; i < Aguja.M; i++) gap = Mathf.Min(gap, aguja.q[i].y - Piso(aguja.q[i].x, aguja.q[i].z) - 0.4f);
                if (gap > 0.02f) { float d = Mathf.Min(gap, 90f * dt); aguja.Mover(Vector3.down * d); caidaAcum += d; }
                else if (caidaAcum > 10f) { caidas++; caidaAcum = 0f; Avisar("Aguja caída", true); }
                else caidaAcum = 0f;
            }

            // ---- hilo ----
            hilo.SinAgarres();
            bool colaFija = aguja.mano >= 0 || enTejido;
            if (colaFija) hilo.Agarrar(0, aguja.Cola);
            for (int m = 0; m < 2; m++) if (agarreHilo[m] >= 0) hilo.Agarrar(agarreHilo[m], h[m].pos);
            hilo.Paso(dt);
            if (!colaFija)
            {
                Vector3 d = hilo.p[0] - aguja.Cola;         // el hilo arrastra a la aguja suelta
                if (d.sqrMagnitude > 1e-4f) aguja.Mover(d);
            }

            // ---- ajuste del punto ----
            tension = 0f;
            if (est == Est.Hilo)
            {
                float cabo = hilo.LibreB;
                if (cabo < 5f)
                {
                    hiloPerdido++; Avisar("Sacaste todo el hilo: el punto se perdió. Repítelo", true);
                    Reponer(); return;
                }
                bool manoB = false;
                for (int m = 0; m < 2; m++) if (agarreHilo[m] >= 0 && agarreHilo[m] * hilo.seg >= hilo.u + hilo.TramoOjales) manoB = true;
                if (hilo.sujetoA && hilo.sujetoB && manoB)
                {
                    tension = hilo.exceso / T_MM;
                    act.tMax = Mathf.Max(act.tMax, tension);
                    if (tension >= 1f)
                    {
                        cortesNudo++; agarreHilo[0] = agarreHilo[1] = -1; sosten = 0f;
                        Avisar("El hilo cortó el parénquima: demasiada tensión al ajustar", true);
                    }
                    else
                    {
                        bool ok = tension >= T_MIN && tension <= T_MAX && cabo <= 80f;
                        sosten = ok ? sosten + dt : Mathf.Max(0f, sosten - dt * 0.5f);
                        if (sosten >= T_SOSTEN) { PuntoHecho(); return; }
                    }
                }
                else sosten = Mathf.Max(0f, sosten - dt * 0.5f);
            }
        }

        protected override void Fondo(float dt, Herr[] h)
        {
            visAguja.Poner(aguja.q, Aguja.M);
            visHilo.Poner(hilo.p, hilo.n);
            for (int i = 0; i < 3; i++)
            {
                bool actual = i == idx && !terminado;
                var a = actual ? mMarcaAct : mMarca; var b = actual ? mMarcaSal : mMarca;
                var ra = mIn[i].GetComponent<MeshRenderer>(); var rb = mOut[i].GetComponent<MeshRenderer>();
                if (ra.sharedMaterial != a) ra.sharedMaterial = a;
                if (rb.sharedMaterial != b) rb.sharedMaterial = b;
                mIn[i].gameObject.SetActive(i >= idx); mOut[i].gameObject.SetActive(i >= idx);
            }
        }

        public override void Panel()
        {
            if (!iniciado) { GUILayout.Label(intro, Hud.texto); return; }
            GUILayout.Label("Punto " + Mathf.Min(idx + 1, 3) + " de 3", Hud.titulo);
            string ins = "";
            switch (est)
            {
                case Est.Fuera:
                    ins = aguja.mano < 0 ? "Toma la aguja a la mitad o dos tercios desde la punta, con la pinza perpendicular a su plano."
                        : "Lleva la punta a la marca AZUL (lado del páncreas) y entra con la punta hacia abajo, perpendicular al tejido."; break;
                case Est.Mal: ins = "Punción fuera de la marca. Retira la aguja por donde entró y vuelve a intentar."; break;
                case Est.Dentro: ins = "Gira la muñeca: la aguja debe avanzar sobre su propia curva hasta salir por la marca BLANCA. No empujes ni hagas palanca."; break;
                case Est.Salio: ins = "Toma la punta con la OTRA mano, suelta la primera y saca la aguja girando sobre su curva."; break;
                case Est.Hilo:
                    ins = hilo.LibreB > CABO_MAX ? "Aleja la aguja para correr el hilo hasta que el cabo libre mida entre " + CABO_MIN.ToString("0") + " y " + CABO_MAX.ToString("0") + " mm."
                        : "Toma el cabo libre con una mano y el hilo (o la aguja) con la otra, y sepáralas hasta la zona verde. Sostén " + T_SOSTEN.ToString("0") + " s."; break;
            }
            GUILayout.Label(ins, Hud.texto);
            if (est == Est.Dentro || est == Est.Salio)
            {
                GUILayout.Label("Desvío respecto de la curva de la aguja: " + desvAhora.ToString("0.0") + " mm", Hud.dato);
                Hud.Barra(desvAhora / (DESV_MAX * 1.5f), 0f, DESV_MAX / (DESV_MAX * 1.5f) * 0.6f);
            }
            if (est == Est.Hilo)
            {
                GUILayout.Label("Cabo libre: " + hilo.LibreB.ToString("0") + " mm", Hud.dato);
                Hud.Barra(hilo.LibreB / 120f, CABO_MIN / 120f, CABO_MAX / 120f);
                GUILayout.Label("Tensión del punto: " + (tension * 100f).ToString("0") + " %", Hud.dato);
                Hud.Barra(tension, T_MIN, T_MAX);
                GUILayout.Label("Sostén: " + sosten.ToString("0.0") + " / " + T_SOSTEN.ToString("0") + " s", Hud.dato);
                Hud.Barra(sosten / T_SOSTEN);
            }
            if (est == Est.Dentro || est == Est.Salio || est == Est.Hilo)
                Hud.Dato("Este punto", "entrada " + act.errIn.ToString("0.0") + " mm · " + act.angIn.ToString("0") + "°" + (est != Est.Dentro ? " · salida " + act.errOut.ToString("0.0") + " mm" : ""));
            if (GUILayout.Button("Reponer aguja (cuenta como error)")) { reposiciones++; Reponer(); }
            Hud.Dato("Tiempo", tiempo.ToString("0.0") + " s");
            Hud.Dato("Punciones fuera de marca", fallidas.ToString());
            Hud.Dato("Desgarros con la aguja", desgarros.ToString());
            Hud.Dato("Retiros / caídas de aguja", retiros + " / " + caidas);
            Hud.Dato("Hilo cortó el tejido", cortesNudo.ToString());
            PanelMensaje();
        }

        protected override void Evaluar()
        {
            int n = Mathf.Max(1, hechos.Count);
            float eI = 0, eO = 0, ang = 0, dv = 0, pr = 0; int sinOtra = 0, caboMal = 0;
            foreach (var p in hechos)
            {
                eI += p.errIn; eO += p.errOut; ang += p.angIn; dv += p.desv; pr += p.prof;
                if (!p.otraMano) sinOtra++;
                if (p.cabo < CABO_MIN || p.cabo > CABO_MAX) caboMal++;
            }
            eI /= n; eO /= n; ang /= n; dv /= n; pr /= n;
            C("Puntos completados", hechos.Count + " / 3", 10f * (3 - hechos.Count), PRECISION, "Completa los tres puntos.");
            C("Error medio de entrada", eI.ToString("0.0") + " mm", Mathf.Min(12f, 2f * Mathf.Max(0f, eI - 2f)), PRECISION,
                "Entraste lejos de la marca azul. Apoya la punta sobre la marca antes de girar; si dudas de la profundidad, mira la sombra de la aguja.");
            C("Error medio de salida", eO.ToString("0.0") + " mm", Mathf.Min(12f, 2f * Mathf.Max(0f, eO - 2.5f)), PRECISION,
                "La aguja salió lejos de la marca blanca. La salida la decide la entrada: si entras perpendicular y giras sobre la curva, la punta sale a un diámetro de aguja de distancia.");
            C("Ángulo medio de entrada", ang.ToString("0") + "° respecto de la perpendicular", Mathf.Min(8f, 0.2f * Mathf.Max(0f, ang - 30f)), PRECISION,
                "Entraste inclinado. La punta debe mirar hacia abajo al tocar el tejido: prona la muñeca antes de entrar.");
            C("Desvío medio de la curva", dv.ToString("0.0") + " mm", Mathf.Min(8f, 3f * Mathf.Max(0f, dv - 1.5f)), TEJIDO,
                "La aguja no giró sobre su propio círculo. El movimiento es solo rotación de la muñeca (eje 6 del mando), sin trasladar la mano.");
            C("Desgarros con la aguja", desgarros.ToString(), 8f * desgarros, TEJIDO,
                "Empujaste o hiciste palanca con la aguja dentro del tejido. En el páncreas eso abre un orificio que fuga.");
            C("Punciones fuera de marca", fallidas.ToString(), 4f * fallidas, PRECISION,
                "Cada punción de más es un orificio de más en el páncreas. Apunta antes de entrar.");
            C("Retiros de la aguja", retiros.ToString(), 2f * retiros, EFICIENCIA, "Sacaste la aguja sin completar el paso.");
            C("Aguja tomada incorrectamente", agarresMalos.ToString(), 2f * agarresMalos, PRECISION,
                "Toma la aguja entre la mitad y los dos tercios desde la punta, y perpendicular a la pinza: así gira sin tambalearse.");
            C("Extracción con la misma mano", sinOtra.ToString(), 3f * sinOtra, BIMANUAL,
                "Sacaste la aguja con la mano que la clavó. La otra mano debe recibir la punta: ahorra tiempo y evita soltar la aguja dentro del campo.");
            C("Caídas / reposiciones de la aguja", caidas + " / " + reposiciones, 5f * caidas + 5f * reposiciones, CONTROL,
                "Perdiste la aguja. Nunca abras una pinza sin que la otra tenga la aguja o sin dejarla apoyada a la vista.");
            C("Hilo extraído por completo", hiloPerdido.ToString(), 6f * hiloPerdido, EFICIENCIA,
                "Corriste todo el hilo y perdiste el punto. Vigila la barra del cabo libre.");
            C("Cabo fuera de medida", caboMal.ToString(), 2f * caboMal, EFICIENCIA,
                "El cabo libre debe quedar entre 2 y 5 cm: más corto no se puede tomar, más largo estorba.");
            C("Hilo cortó el tejido al ajustar", cortesNudo.ToString(), 8f * cortesNudo, TEJIDO,
                "Ajustaste con demasiada tensión. En el páncreas se aproxima, no se estrangula: detente en la zona verde.");
            C("Pinzamientos del muñón", pinzamientos.ToString(), 5f * pinzamientos, TEJIDO, "No tomes el páncreas con la pinza.");
            C("Profundidad media del punto", pr.ToString("0.0") + " mm", pr < 1.5f && hechos.Count > 0 ? 4f : 0f, PRECISION,
                "Puntos muy superficiales: toma al menos 2–3 mm de tejido para que el punto no se desgarre.");
        }
    }
}
