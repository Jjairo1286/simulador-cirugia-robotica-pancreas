using UnityEngine;

namespace BrazosRobot
{
    /// <summary>NIVEL 1 — Transferencia de anillos (tipo FLS): la práctica con física que ya existía.</summary>
    public class NivelAnillos : Nivel
    {
        readonly Practica practica; readonly Pinza[] pinza;

        public NivelAnillos(Practica practica, Pinza[] pinza)
        {
            this.practica = practica; this.pinza = pinza;
            numero = 1; corto = "Anillos"; titulo = "Transferencia de anillos";
            intro = "Pasa los 6 anillos rosados de los postes blancos (izquierda) a los verdes (derecha). Tómalos con la pinza izquierda y pásalos a la derecha en el aire. Sirve para acostumbrarse al mando, a la profundidad y al embrague.";
            tiempoPar = 150f; recorridoPar = 260f;
            rol = new[] { "agarre y transferencia", "agarre y transferencia" };
            origen = Vector3.zero; inclinacion = 0f;
            vista = new Orbita.Vista(Mathf.PI, 1.09f, 105, 46, new Vector3(10, -8, 0));
        }

        protected override void Construir()
        {
            practica.raiz.gameObject.SetActive(true);
            practica.Reiniciar(pinza[0], pinza[1]);
        }

        public override void Desmontar()
        {
            practica.Reiniciar(pinza[0], pinza[1]);
            practica.raiz.gameObject.SetActive(false);
            base.Desmontar();
        }

        protected override void Fondo(float dt, Herr[] h) { practica.Actualizar(dt); }
        protected override void Paso(float dt, Herr[] h) { if (practica.enDestino >= 6) Terminar(); }

        public override void Panel()
        {
            GUILayout.Label(intro, Hud.texto);
            GUILayout.Label("Anillos en los postes verdes: " + practica.enDestino + " / 6", Hud.texto);
            Hud.Barra(practica.enDestino / 6f);
            Hud.Dato("Tiempo", tiempo.ToString("0.0") + " s");
            Hud.Dato("Recorrido de las puntas", ((recorrido[0] + recorrido[1]) / 10f).ToString("0") + " cm");
            Hud.Dato("Choques entre instrumentos", colisiones.ToString());
            PanelMensaje();
        }

        protected override void Evaluar()
        {
            C("Anillos colocados", practica.enDestino + " / 6", 10f * (6 - practica.enDestino), PRECISION, "Completa los seis anillos.");
        }
    }

    /// <summary>
    /// NIVEL 2 — Manipulación de tejido: exposición y suspensión del páncreas.
    ///
    /// Es el ejercicio de manipulación de tejido original, ahora con los dos brazos: una mano levanta el
    /// estómago y MANTIENE la exposición (en la versión de un brazo lo sostenía el "4.º brazo") mientras
    /// la otra entra a la transcavidad y suspende el cuello del páncreas con el lazo vascular.
    /// El páncreas es friable y nunca se pinza directamente: se tracciona del lazo.
    /// </summary>
    public class NivelTejido : Nivel
    {
        enum Fase { Estomago, Lazo, BajarPancreas, BajarEstomago }
        const float E_MIN = 0.35f, E_MAX = 0.85f, E_SOSTEN = 2f;
        const float P_MIN = 0.30f, P_MAX = 0.80f, P_SOSTEN = 2.5f;
        const float SUAVE = 0.45f, V_TIRON = 250f;
        static readonly Vector3 C_EST = new Vector3(4, 26, 24), R_EST = new Vector3(36, 14, 52);
        static readonly Vector3 CUELLO = new Vector3(0, Anatomia.CUELLO_Y + Anatomia.CUELLO_B, 0);
        static readonly Vector3 ARRIBA_EST = new Vector3(0.5f, 0.866f, 0f);

        Tejido estomago, pancreas;
        Transform marca, lazo; Vector3 marca0, asa0;
        Fase fase;
        int manoE = -1, manoP = -1; Vector3 offE, offP;
        float sosten, exceso, maxE, maxP, enfria, expoPerdida;
        int desgarros, prohibidos, tirones, bruscas, perdidas;

        public NivelTejido()
        {
            numero = 2; corto = "Tejido"; titulo = "Manipulación de tejido: exposición y suspensión del páncreas";
            intro = "El páncreas está detrás del estómago. Con una mano levanta el estómago y mantenlo; con la otra entra por debajo, toma el LAZO azul que rodea el cuello del páncreas (nunca el páncreas) y suspéndelo. No hay sensación de fuerza: guíate por las barras de tensión y por el color del tejido (pálido = demasiada tracción).";
            tiempoPar = 120f; recorridoPar = 110f;
            herramienta = new[] { TipoInstrumento.Fenestrada, TipoInstrumento.Cadiere };
            rol = new[] { "toma el lazo y suspende el páncreas", "retrae el estómago" };
            // el borde del estómago queda al centro y las pinzas empiezan por fuera de él
            Vector3 r = Quaternion.AngleAxis(inclinacion, Vector3.forward) * new Vector3(-25, 16, 5);
            origen = new Vector3(22, -18, 0) - r;
            vista = new Orbita.Vista(Mathf.PI, 1.09f, 108, 48, new Vector3(18, -14, 0));
        }

        protected override void Construir()
        {
            anat = new Anatomia(raiz);
            pancreas = new Tejido("páncreas", anat.goPancreas.GetComponent<MeshFilter>().sharedMesh, anat.mPancreas, 22f, 24f);

            var mE = Geo.Mat(0xd98a7c, 0f, 0.4f);
            var malla = Mallas.Elipsoide(C_EST, R_EST, 36, 22, 0.03f);
            Geo.Obj(malla, mE, raiz, "Estómago");
            estomago = new Tejido("estómago", malla, mE, 60f, 42f) { ancla = new Vector3(36, 28, 40), radioAncla = 60f };
            anat.etiquetas.Add(new Etiqueta("Estómago (curvatura mayor)", C_EST + new Vector3(-14, 13, 0)));

            // marca verde: zona sugerida de agarre, en la cara posterior de la curvatura mayor
            Vector3 u = new Vector3(-0.72f, -0.62f, -0.30f).normalized;
            marca0 = C_EST + Vector3.Scale(u, R_EST);
            marca = Geo.Sphere(2.6f, Geo.Mat(0x30e050, 0f, 0.6f), raiz, marca0).transform;
            marca.name = "Zona de agarre";

            lazo = CrearLazo(0f, Anatomia.CUELLO_Y, Anatomia.CUELLO_A, Anatomia.CUELLO_B, 0f, out asa0);
            anat.etiquetas.Add(new Etiqueta("Lazo vascular", asa0 + new Vector3(0, 3, 0)));
        }

        static float FEst(Vector3 q)
        {
            Vector3 d = q - C_EST;
            return Mathf.Sqrt(d.x * d.x / (R_EST.x * R_EST.x) + d.y * d.y / (R_EST.y * R_EST.y) + d.z * d.z / (R_EST.z * R_EST.z));
        }

        Vector3 Asa() { return asa0 + pancreas.Mover(CUELLO) - CUELLO; }

        void SoltarE()
        {
            float t = estomago.tension; estomago.Soltar(); manoE = -1;
            if (fase == Fase.BajarEstomago)
            {
                if (t > SUAVE) { bruscas++; Avisar("Liberación brusca del estómago", true); }
                Terminar();
                return;
            }
            if (t > 0.5f) { bruscas++; Avisar("Liberación brusca: baja la tensión antes de soltar", true); }
            if (fase != Fase.Estomago) { perdidas++; Avisar("Perdiste la exposición: vuelve a levantar el estómago", true); }
        }

        void SoltarP()
        {
            float t = pancreas.tension; pancreas.Soltar(); manoP = -1;
            if (fase == Fase.BajarPancreas)
            {
                if (t > SUAVE) { bruscas++; Avisar("Liberación brusca del páncreas", true); }
                fase = Fase.BajarEstomago;
                if (manoE < 0) Terminar();
                else Avisar("Ahora baja el estómago despacio y suéltalo", false);
                return;
            }
            if (t > 0.5f) { bruscas++; Avisar("Liberación brusca: baja la tensión antes de soltar", true); }
        }

        void Cerro(Herr t)
        {
            int m = t.mano;
            if (manoE == m || manoP == m) return;
            if (manoP < 0 && (t.pos - Asa()).magnitude < 9f)
            {
                manoP = m; pancreas.Iniciar(CUELLO); offP = t.pos - CUELLO;
                if (fase == Fase.Estomago) Avisar("Primero expón: levanta el estómago con la otra mano", true);
                return;
            }
            Vector3 q = t.pos - (estomago.Mover(t.pos) - t.pos);      // punto equivalente en reposo
            if (manoE < 0 && FEst(q) < 1.3f)
            {
                manoE = m; estomago.Iniciar(q); offE = t.pos - q;
                return;
            }
            float dv; var vaso = anat.VasoCercano(t.pos, out dv);
            if (pancreas.agarrado == false && anat.pancreas.Dist(t.pos - (pancreas.Mover(t.pos) - t.pos)) < -1f)
            { prohibidos++; pancreas.Lesion(); Avisar("LESIÓN: no pinces el páncreas, toma el lazo", true); }
            else if (anat.EnBazo(t.pos)) { prohibidos++; Avisar("LESIÓN: no pinces el bazo", true); }
            else if (anat.EnHigado(t.pos)) { prohibidos++; Avisar("LESIÓN: no pinces el hígado", true); }
            else if (vaso != null && dv < -0.5f) { prohibidos++; Avisar("LESIÓN: pinzaste la " + vaso.nombre, true); }
        }

        protected override void Paso(float dt, Herr[] h)
        {
            enfria -= dt;
            foreach (var t in h)
            {
                if (t.cerro) Cerro(t);
                if (t.abrio) { if (manoE == t.mano) SoltarE(); else if (manoP == t.mano) SoltarP(); }
                if (terminado) return;
            }
            if (manoE >= 0)
            {
                if (estomago.Arrastrar(h[manoE].pos - offE))
                {
                    desgarros++; manoE = -1; sosten = 0f;
                    if (fase != Fase.Estomago) perdidas++;
                    Avisar("DESGARRO del estómago: tracción excesiva", true);
                    if (fase == Fase.BajarEstomago) { Terminar(); return; }
                }
                maxE = Mathf.Max(maxE, estomago.tension);
            }
            if (manoP >= 0)
            {
                if (pancreas.Arrastrar(h[manoP].pos - offP))
                {
                    desgarros++; manoP = -1; sosten = 0f; Avisar("DESGARRO del páncreas: tracción excesiva del lazo", true);
                    if (fase == Fase.BajarPancreas) { fase = Fase.BajarEstomago; if (manoE < 0) { Terminar(); return; } }
                }
                maxP = Mathf.Max(maxP, pancreas.tension);
            }
            for (int m = 0; m < 2; m++)
                if ((m == manoE || m == manoP) && h[m].rapidez > V_TIRON && enfria <= 0f)
                { tirones++; enfria = 1f; Avisar("Tirón brusco: mueve el tejido lentamente", true); }
            if (manoE >= 0 && estomago.tension > E_MAX) exceso += dt;
            if (manoP >= 0 && pancreas.tension > (fase == Fase.BajarPancreas ? 1f : P_MAX)) exceso += dt;

            bool eOk = manoE >= 0 && estomago.tension >= E_MIN && estomago.tension <= E_MAX
                       && Vector3.Dot(estomago.desp.normalized, ARRIBA_EST) > 0.4f;
            if (fase == Fase.Estomago)
            {
                sosten = eOk ? sosten + dt : Mathf.Max(0f, sosten - dt * 0.25f);
                if (sosten >= E_SOSTEN)
                {
                    fase = Fase.Lazo; sosten = 0f;
                    Avisar("Exposición lograda. MANTÉN el estómago y toma el lazo con la otra mano", false);
                }
            }
            else if (fase == Fase.Lazo)
            {
                if (!eOk) expoPerdida += dt;
                bool pOk = manoP >= 0 && pancreas.tension >= P_MIN && pancreas.tension <= P_MAX
                           && pancreas.desp.y > 0.3f * pancreas.desp.magnitude;
                sosten = (eOk && pOk) ? sosten + dt : Mathf.Max(0f, sosten - dt * 0.25f);
                if (sosten >= P_SOSTEN)
                {
                    fase = Fase.BajarPancreas; sosten = 0f;
                    Avisar("Bien. Regresa el páncreas a su sitio y suelta el lazo con suavidad", false);
                }
            }
        }

        protected override void Fondo(float dt, Herr[] h)
        {
            estomago.Paso(dt); pancreas.Paso(dt);
            marca.localPosition = estomago.Mover(marca0);
            marca.gameObject.SetActive(fase == Fase.Estomago && manoE < 0 && !terminado);
            lazo.localPosition = pancreas.Mover(CUELLO) - CUELLO;
        }

        public override void Panel()
        {
            if (!iniciado) { GUILayout.Label(intro, Hud.texto); return; }
            string ins = "";
            switch (fase)
            {
                case Fase.Estomago: ins = "FASE 1 — Toma el estómago en la marca verde y levántalo hacia arriba y adelante hasta la zona verde. Sostén " + E_SOSTEN.ToString("0") + " s."; break;
                case Fase.Lazo: ins = "FASE 2 — Sin soltar el estómago, lleva la otra pinza por debajo hasta el cabo blanco del lazo azul. Toma el LAZO y levanta el cuello del páncreas hasta la zona verde. Sostén " + P_SOSTEN.ToString("0") + " s con las dos tensiones en verde."; break;
                case Fase.BajarPancreas: ins = "FASE 3 — Regresa el páncreas lentamente y suelta el lazo con tensión menor a " + (SUAVE * 100f).ToString("0") + " %. Sigue sosteniendo el estómago."; break;
                case Fase.BajarEstomago: ins = "FASE 4 — Baja el estómago y suéltalo con tensión menor a " + (SUAVE * 100f).ToString("0") + " %."; break;
            }
            GUILayout.Label(ins, Hud.texto);
            GUILayout.Label("Tracción del estómago: " + (estomago.tension * 100f).ToString("0") + " %" + (manoE >= 0 ? "  (mano " + MANO[manoE] + ")" : ""), Hud.dato);
            if (fase == Fase.BajarEstomago) Hud.Barra(estomago.tension, 0f, SUAVE); else Hud.Barra(estomago.tension, E_MIN, E_MAX);
            GUILayout.Label("Suspensión del páncreas: " + (pancreas.tension * 100f).ToString("0") + " %" + (manoP >= 0 ? "  (mano " + MANO[manoP] + ")" : ""), Hud.dato);
            if (fase == Fase.BajarPancreas) Hud.Barra(pancreas.tension, 0f, SUAVE); else Hud.Barra(pancreas.tension, P_MIN, P_MAX);
            if (fase == Fase.Estomago || fase == Fase.Lazo)
            {
                float obj = fase == Fase.Estomago ? E_SOSTEN : P_SOSTEN;
                GUILayout.Label("Sostén: " + sosten.ToString("0.0") + " / " + obj.ToString("0") + " s", Hud.dato);
                Hud.Barra(sosten / obj);
            }
            Hud.Dato("Tiempo", tiempo.ToString("0.0") + " s");
            Hud.Dato("Desgarros", desgarros.ToString());
            Hud.Dato("Agarres de órgano frágil", prohibidos.ToString());
            Hud.Dato("Tirones bruscos", tirones.ToString());
            Hud.Dato("Liberaciones bruscas", bruscas.ToString());
            Hud.Dato("Tracción excesiva", exceso.ToString("0.0") + " s");
            Hud.Dato("Exposición perdida", perdidas + " veces · " + expoPerdida.ToString("0.0") + " s");
            PanelMensaje();
        }

        protected override void Evaluar()
        {
            // Calificación simplificada: desgarro, órgano frágil, tracción excesiva, movimiento brusco y el error propio del nivel.
            C("Desgarros", desgarros.ToString(), 20f * desgarros, TEJIDO,
                "Hubo desgarro: la tensión llegó al 100 %. Detente en la zona verde; si el tejido palidece, afloja.");
            C("Agarres de órgano frágil", prohibidos.ToString(), 15f * prohibidos, TEJIDO,
                "Pinzaste un órgano que no se toma (páncreas, bazo, hígado o un vaso). El páncreas se moviliza con el lazo, nunca con la pinza.");
            C("Tiempo con tracción excesiva", exceso.ToString("0.0") + " s", Mathf.Min(20f, 2f * exceso), TEJIDO,
                "Pasaste tiempo por encima de la zona verde: la tracción sostenida produce isquemia aunque no llegue a desgarrar.");
            C("Movimientos bruscos", (tirones + bruscas).ToString(), 5f * (tirones + bruscas), TEJIDO,
                "Moviste o soltaste el tejido de golpe. Con tejido tomado avanza despacio, y abre la pinza solo con la barra por debajo del 45 %.");
            C("Exposición perdida", perdidas + " veces · " + expoPerdida.ToString("0.0") + " s", Mathf.Min(20f, 4f * perdidas + 1.5f * expoPerdida), BIMANUAL,
                "La mano que sostiene el estómago se movió mientras trabajaba la otra. Fija esa mano (embrague o apoyo): es la coordinación bimanual que pide este nivel.");
            C("Tensión máxima", "estómago " + (maxE * 100f).ToString("0") + " % · páncreas " + (maxP * 100f).ToString("0") + " %", 0f, TEJIDO, "");
        }
    }
}
