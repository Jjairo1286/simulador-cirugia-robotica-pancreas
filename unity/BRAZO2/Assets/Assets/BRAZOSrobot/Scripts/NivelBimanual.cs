using UnityEngine;

namespace BrazosRobot
{
    /// <summary>
    /// NIVEL 3 — Trabajo bimanual: paso de la cinta por el túnel retropancreático y suspensión del cuello.
    ///
    /// Antes de seccionar el páncreas se labra un túnel entre la cara posterior del cuello y la cara
    /// anterior de la vena mesentérica superior / porta, y se pasa una cinta para suspenderlo.
    /// Aquí una mano lleva la punta de la cinta por el túnel (de caudal a craneal, sin tocar la vena que
    /// queda debajo ni el páncreas que queda encima), la otra la recibe al otro lado, y luego las dos
    /// levantan el cuello a la vez con tensión controlada. Ningún paso puede hacerse con una sola mano.
    /// </summary>
    public class NivelBimanual : Nivel
    {
        enum Fase { Tomar, Pasar, Recibir, Centrar, Suspender, Bajar }
        const float XE = -11.5f, XS = 11.5f;                                    // bocas caudal y craneal del túnel
        const float Y_VENA = Anatomia.VMS_Y + Anatomia.VMS_R;                    // techo de la vena (suelo del túnel)
        const float Y_PANC = Anatomia.CUELLO_Y - Anatomia.CUELLO_B;              // cara posterior del cuello (techo del túnel)
        const float YT = (Y_VENA + Y_PANC) * 0.5f;
        const float T_MIN = 0.30f, T_MAX = 0.85f, SOSTEN = 3f, SUAVE = 0.45f, DMAX = 22f;
        static readonly Vector3 CUELLO = new Vector3(0, Anatomia.CUELLO_Y + Anatomia.CUELLO_B, 0);

        Tejido pancreas; Hilo cinta; TuboDin vis; Transform mPunta, mCentro, guias;
        Sangrado sangre;
        Fase fase;
        readonly int[] agarre = { -1, -1 };
        readonly bool[] tocando = new bool[2];
        int manoPasa = -1; bool enTunel, ojales;
        Vector3 d;
        float avance, sosten, exceso, contactoT, presionT, enfriaLesion, enfriaFalsa, angSuma, angT, centradoFinal = -1f, maxT;
        int contactos, lesiones, falsas, caidas, salidas, desgarros, bruscas;

        public NivelBimanual()
        {
            numero = 3; corto = "Bimanual"; titulo = "Trabajo bimanual: cinta por el túnel retropancreático";
            intro = "Pasa la cinta azul por detrás del cuello del páncreas. Una mano lleva la punta blanca por el túnel, entre el páncreas (arriba) y la vena mesentérica superior (abajo); la otra la recibe en el borde superior. Después centra la cinta y, con un cabo en cada mano, suspende el cuello. El páncreas se vuelve translúcido cuando trabajas debajo de él.";
            tiempoPar = 180f; recorridoPar = 150f;
            herramienta = new[] { TipoInstrumento.Cadiere, TipoInstrumento.Maryland };
            rol = new[] { "recibe la cinta y tracciona", "pasa la cinta por el túnel (punta curva)" };
            Centrar(new Vector3(-2, 0, 6), 105f, 42f);
        }

        protected override void Construir()
        {
            anat = new Anatomia(raiz);
            pancreas = new Tejido("páncreas", anat.goPancreas.GetComponent<MeshFilter>().sharedMesh, anat.mPancreas, DMAX, 22f);
            sangre = new Sangrado(raiz);
            cinta = new Hilo(61, 2.5f) { radio = 0.9f, suelo = anat.Suelo };
            vis = new TuboDin(raiz, "Cinta", Geo.Mat(0x2a8cf0, 0f, 0.6f), 0.9f, 6);
            mPunta = Geo.Sphere(1.5f, Geo.Mat(0xffffff, 0f, 0.6f), raiz, Vector3.zero).transform;
            mCentro = Geo.Sphere(1.4f, Geo.Mat(0xffd21f, 0f, 0.6f), raiz, Vector3.zero).transform;
            guias = Geo.Grupo(raiz, "Bocas del túnel", Vector3.zero);
            var mg = Geo.Mat(0x30e050, 0f, 0.6f);
            foreach (float x in new[] { XE - 0.6f, XS + 0.6f })
            {
                var a = Geo.Obj(Geo.Torus(3.3f, 0.3f, 24, 8), mg, guias, "boca");
                a.transform.localPosition = new Vector3(x, YT, 0f);
                a.transform.localRotation = Quaternion.AngleAxis(90f, Vector3.forward);
            }
            anat.etiquetas.Add(new Etiqueta("Túnel retropancreático", new Vector3(XE - 2f, YT, 0f)));
            Reponer();
        }

        /// Deja la cinta tendida sobre la grasa, a la izquierda del paciente y lejos de los vasos.
        void Reponer()
        {
            cinta.QuitarOjales(); cinta.SinAgarres(); ojales = false; enTunel = false; avance = 0f;
            agarre[0] = agarre[1] = -1;
            for (int k = 0; k < cinta.n; k++)
            {
                float s = k * cinta.seg; Vector3 q;
                if (s < 45f) q = new Vector3(-24f, 0f, 34f + s);
                else if (s < 53f) q = new Vector3(-24f - (s - 45f), 0f, 79f);
                else if (s < 98f) q = new Vector3(-32f, 0f, 79f - (s - 53f));
                else if (s < 106f) q = new Vector3(-32f - (s - 98f), 0f, 34f);
                else q = new Vector3(-40f, 0f, 34f + (s - 106f));
                q.y = anat.Suelo(q.x, q.z) + cinta.radio;
                cinta.p[k] = q;
            }
        }

        void Soltar(int m)
        {
            int k = agarre[m]; agarre[m] = -1;
            if (fase == Fase.Pasar && k <= 3 && enTunel) { caidas++; Avisar("Soltaste la cinta dentro del túnel", true); }
            else if (fase == Fase.Recibir && m == manoPasa && !(agarre[1 - m] >= 0 && agarre[1 - m] <= 12))
            { caidas++; Avisar("Soltaste la punta antes de que la otra pinza la tomara", true); }
            else if ((fase == Fase.Suspender || fase == Fase.Bajar) && d.magnitude / DMAX > (fase == Fase.Bajar ? SUAVE : 0.5f))
            { bruscas++; Avisar("Liberación brusca: baja el cuello antes de soltar", true); }
        }

        protected override void Paso(float dt, Herr[] h)
        {
            enfriaLesion -= dt; enfriaFalsa -= dt;

            // ---- contactos con los vasos y con el parénquima ----
            bool bajo = false;
            foreach (var t in h)
            {
                float d1, d2;
                var v1 = anat.VasoCercano(t.punta, out d1); anat.VasoCercano(t.pos, out d2);
                float sd = Mathf.Min(d1, d2);
                bool toca = sd < -0.6f;
                if (toca) contactoT += dt;
                if (toca && !tocando[t.mano]) { contactos++; Avisar("Contacto con la " + v1.nombre, true); }
                tocando[t.mano] = sd < 0f && (toca || tocando[t.mano]);
                if (sd < -3.5f && enfriaLesion <= 0f)
                {
                    lesiones++; enfriaLesion = 3f;
                    sangre.Abrir(t.punta, v1.mlMin, v1.nombre);
                    Avisar("LESIÓN de la " + v1.nombre + ": comprime 2 s con la pinza cerrada", true);
                }
                Vector3 rep = t.pos - (pancreas.Mover(t.pos) - t.pos);
                float dp = anat.pancreas.Dist(rep);
                if (dp < -2.5f) presionT += dt;
                if (dp < -4.5f && enfriaFalsa <= 0f && agarre[t.mano] >= 0 && agarre[t.mano] <= 3)
                { falsas++; enfriaFalsa = 2f; Avisar("Falsa vía: la punta entró al páncreas. Busca el plano sobre la vena", true); }
                if (t.punta.x > XE - 9f && t.punta.x < XS + 9f && Mathf.Abs(t.punta.z) < 12f && t.punta.y < Y_PANC + 4f) bajo = true;
            }
            anat.Translucido(bajo);
            sangre.Paso(dt, h);

            // ---- agarres de la cinta ----
            foreach (var t in h)
            {
                if (t.cerro && agarre[t.mano] < 0)
                {
                    float dd; int k = cinta.Cercano(t.pos, out dd);
                    if (dd < 7f && !cinta.EnOjales(k)) agarre[t.mano] = k;
                    else if (anat.pancreas.Dist(t.pos) < -1.5f && !bajo) { falsas++; pancreas.Lesion(); Avisar("LESIÓN: no pinces el páncreas", true); }
                }
                if (t.abrio && agarre[t.mano] >= 0) Soltar(t.mano);
            }
            cinta.SinAgarres();
            for (int m = 0; m < 2; m++) if (agarre[m] >= 0) cinta.Agarrar(agarre[m], h[m].pos);
            if (ojales) { cinta.ojal[0] = new Vector3(XS, YT, 0f) + d; cinta.ojal[1] = new Vector3(XE, YT, 0f) + d; }
            cinta.Paso(dt);

            // ---- el cuello cuelga de la cinta ----
            if (ojales && cinta.sujetoA && cinta.sujetoB)
            {
                Vector3 sum = (cinta.posA - cinta.ojal[0]).normalized + (cinta.posB - cinta.ojal[1]).normalized;
                float sm = Mathf.Max(0.6f, sum.magnitude);
                Vector3 sdir = sum.sqrMagnitude > 1e-6f ? sum.normalized : Vector3.up;
                if (cinta.exceso > 0.01f) d += sdir * (cinta.exceso / sm * 0.9f);
                else if (cinta.holgura > 0.01f) d = Vector3.MoveTowards(d, Vector3.zero, Mathf.Min(cinta.holgura / sm * 0.9f, 80f * dt));
            }
            else d = Vector3.MoveTowards(d, Vector3.zero, 80f * dt);
            float tension = d.magnitude / DMAX;
            maxT = Mathf.Max(maxT, tension);
            if (pancreas.Imponer(CUELLO, d))
            {
                desgarros++; agarre[0] = agarre[1] = -1; sosten = 0f; d = d.normalized * DMAX * 0.6f;
                Avisar("DESGARRO: tracción excesiva del cuello (arranca las venas pancreáticas)", true);
            }

            // ---- fases ----
            Vector3 c = cinta.p[0];
            int mh = (agarre[0] >= 0 && agarre[0] <= 3) ? 0 : ((agarre[1] >= 0 && agarre[1] <= 3) ? 1 : -1);
            bool corredor = c.x > XE - 4f && c.x < XS + 5f && Mathf.Abs(c.z) < 8.5f && c.y > Y_VENA - 2.5f && c.y < Y_PANC + 2.5f;
            float desc = ojales ? (cinta.u + cinta.TramoOjales * 0.5f) - cinta.largo * 0.5f : 0f;
            switch (fase)
            {
                case Fase.Tomar:
                    if (mh >= 0) { fase = Fase.Pasar; manoPasa = mh; enTunel = corredor; }
                    break;
                case Fase.Pasar:
                    if (mh < 0) { fase = Fase.Tomar; enTunel = false; break; }
                    manoPasa = mh;
                    if (!enTunel) { if (corredor && c.x < XE + 7f) enTunel = true; }
                    else if (!corredor) enTunel = false;
                    if (enTunel)
                    {
                        avance = Mathf.Max(avance, Mathf.InverseLerp(XE, XS, c.x));
                        if (c.x > XS + 1f)
                        {
                            fase = Fase.Recibir; avance = 1f;
                            Avisar("La punta salió por el borde superior: recíbela con la otra mano", false);
                        }
                    }
                    else avance = 0f;
                    break;
                case Fase.Recibir:
                    {
                        int otra = 1 - manoPasa;
                        bool tOtra = agarre[otra] >= 0 && agarre[otra] <= 12, tPasa = agarre[manoPasa] >= 0 && agarre[manoPasa] <= 12;
                        if (c.x < XS - 2f) { fase = mh >= 0 ? Fase.Pasar : Fase.Tomar; enTunel = corredor; break; }
                        if (tOtra && !tPasa)
                        {
                            float dd; int k = cinta.Cercano(new Vector3(XS, YT, 0f), out dd);
                            // longitud de cinta entre la pinza que recibe y la boca craneal: al menos la distancia real
                            float u0 = Mathf.Max(k * cinta.seg, agarre[otra] * cinta.seg + (h[otra].pos - new Vector3(XS, YT, 0f)).magnitude + 1f, 6f);
                            cinta.PonerOjales(new[] { new Vector3(XS, YT, 0f), new Vector3(XE, YT, 0f) }, u0);
                            ojales = true; fase = Fase.Centrar;
                            Avisar("Transferencia hecha. Tira hasta que la marca amarilla quede en el túnel", false);
                        }
                    }
                    break;
                case Fase.Centrar:
                    if (Mathf.Abs(desc) <= 18f)
                    {
                        fase = Fase.Suspender;
                        Avisar("Cinta centrada. Toma un cabo con cada mano y levanta el cuello", false);
                    }
                    break;
                case Fase.Suspender:
                    {
                        bool ambos = cinta.sujetoA && cinta.sujetoB;
                        if (ambos && tension > T_MAX) exceso += dt;
                        if (ambos && d.magnitude > 2f) { angSuma += Vector3.Angle(d, Vector3.up) * dt; angT += dt; }
                        bool ok = ambos && tension >= T_MIN && tension <= T_MAX && d.y > 0.6f * d.magnitude && Mathf.Abs(desc) <= 35f;
                        sosten = ok ? sosten + dt : Mathf.Max(0f, sosten - dt * 0.25f);
                        if (sosten >= SOSTEN)
                        {
                            fase = Fase.Bajar; centradoFinal = Mathf.Abs(desc);
                            Avisar("Suspensión lograda. Baja el cuello y suelta los dos cabos", false);
                        }
                    }
                    break;
                case Fase.Bajar:
                    if (agarre[0] < 0 && agarre[1] < 0 && !sangre.Activo) { Terminar(); return; }
                    break;
            }
            if (ojales && (cinta.LibreA < 2f || cinta.LibreB < 2f))
            {
                salidas++; Reponer(); fase = Fase.Tomar; sosten = 0f;
                Avisar("La cinta se salió del túnel: vuelve a pasarla", true);
            }
        }

        protected override void Fondo(float dt, Herr[] h)
        {
            pancreas.Paso(dt);
            vis.Poner(cinta.p, cinta.n);
            mPunta.localPosition = cinta.p[0];
            mCentro.localPosition = cinta.p[cinta.n / 2];
            guias.localPosition = d;
            guias.gameObject.SetActive(fase <= Fase.Recibir && !terminado);
        }

        public override void Panel()
        {
            if (!iniciado) { GUILayout.Label(intro, Hud.texto); return; }
            float tension = d.magnitude / DMAX;
            float desc = ojales ? (cinta.u + cinta.TramoOjales * 0.5f) - cinta.largo * 0.5f : 0f;
            string ins = "";
            switch (fase)
            {
                case Fase.Tomar: ins = "FASE 1 — Toma la cinta por su punta blanca (abajo a la derecha de la imagen)."; break;
                case Fase.Pasar: ins = "FASE 2 — Lleva la punta a la boca caudal del túnel (aro verde más cercano) y avanza hacia arriba por debajo del cuello, pegado al plano: ni la vena (abajo) ni el páncreas (arriba)."; break;
                case Fase.Recibir: ins = "FASE 3 — Con la OTRA mano toma la punta que asoma por el borde superior. Suelta la primera solo cuando la segunda ya la tenga."; break;
                case Fase.Centrar: ins = "FASE 4 — Tira de la cinta hasta que la marca amarilla (su centro) quede dentro del túnel. Si te pasas, tira del otro cabo."; break;
                case Fase.Suspender: ins = "FASE 5 — Un cabo en cada mano. Levanta las dos manos a la vez, hacia arriba, hasta la zona verde y sostén " + SOSTEN.ToString("0") + " s."; break;
                case Fase.Bajar: ins = "FASE 6 — Baja el cuello hasta menos de " + (SUAVE * 100f).ToString("0") + " % y suelta los dos cabos."; break;
            }
            GUILayout.Label(ins, Hud.texto);
            if (fase == Fase.Pasar || fase == Fase.Recibir)
            {
                GUILayout.Label("Avance por el túnel: " + (avance * 100f).ToString("0") + " %", Hud.dato);
                Hud.Barra(avance);
            }
            if (ojales)
            {
                GUILayout.Label("Centrado de la cinta: " + desc.ToString("+0;-0;0") + " mm", Hud.dato);
                Hud.Barra(0.5f + desc / 120f, 0.5f - 18f / 120f, 0.5f + 18f / 120f);
                GUILayout.Label("Suspensión del cuello: " + (tension * 100f).ToString("0") + " %", Hud.dato);
                if (fase == Fase.Bajar) Hud.Barra(tension, 0f, SUAVE); else Hud.Barra(tension, T_MIN, T_MAX);
            }
            if (fase == Fase.Suspender)
            {
                GUILayout.Label("Sostén: " + sosten.ToString("0.0") + " / " + SOSTEN.ToString("0") + " s", Hud.dato);
                Hud.Barra(sosten / SOSTEN);
            }
            if (sangre.Activo)
            {
                Hud.Tinte("SANGRADO: comprime con la pinza cerrada sobre el foco", new Color(1f, 0.4f, 0.35f), Hud.titulo);
                Hud.Barra(sangre.Progreso);
            }
            Hud.Dato("Tiempo", tiempo.ToString("0.0") + " s");
            Hud.Dato("Contactos con vasos", contactos + " · " + contactoT.ToString("0.0") + " s");
            Hud.Dato("Lesiones vasculares", lesiones.ToString());
            Hud.Dato("Pérdida de sangre", sangre.ml.ToString("0") + " ml");
            Hud.Dato("Falsas vías / pinzamientos", falsas.ToString());
            Hud.Dato("Cinta soltada", caidas.ToString());
            Hud.Dato("Tracción excesiva", exceso.ToString("0.0") + " s");
            PanelMensaje();
        }

        protected override void Evaluar()
        {
            // Calificación simplificada: desgarro, lesión vascular, contactos, tracción excesiva, movimiento brusco y el error propio del nivel.
            C("Desgarros", desgarros.ToString(), 20f * desgarros, TEJIDO,
                "La tracción llegó al 100 %. Quédate en la zona verde.");
            C("Lesiones vasculares", lesiones.ToString(), 25f * lesiones, PRECISION,
                "Perforaste un vaso mayor. En cirugía real una lesión de la vena mesentérica superior o la porta obliga a reparar o convertir: nunca empujes a ciegas.");
            C("Contactos con vasos o con el páncreas", (contactos + falsas).ToString(), Mathf.Min(18f, 3f * (contactos + falsas)), PRECISION,
                "Tocaste la vena o entraste al páncreas al pasar. El túnel tiene unos 6 mm de alto: entra con la muñeca horizontal y avanza por el centro.");
            C("Tiempo con tracción excesiva", exceso.ToString("0.0") + " s", Mathf.Min(16f, 2f * exceso), TEJIDO,
                "Mantuviste el cuello por encima de la zona verde.");
            C("Movimientos bruscos", bruscas.ToString(), 5f * bruscas, TEJIDO,
                "Soltaste con el cuello aún suspendido: bájalo primero.");
            C("Cinta soltada o fuera del túnel", (caidas + salidas).ToString(), 5f * (caidas + salidas), BIMANUAL,
                "En una transferencia la mano que entrega no abre hasta ver que la otra ya cerró, y la cinta debe quedarse en el túnel.");
            C("Tensión máxima", (maxT * 100f).ToString("0") + " %", 0f, TEJIDO, "");
        }
    }
}
