using System.Collections.Generic;
using UnityEngine;

namespace BrazosRobot
{
    /// <summary>
    /// NIVEL 4 — Disección cerca de vasos: exposición de la vena mesentérica superior en el borde inferior
    /// del cuello del páncreas.
    ///
    /// La vena está cubierta por una lámina de tejido fibroadiposo. La mano no dominante tracciona el
    /// mesocolon (tracción–contratracción) para tensar esa lámina; la dominante la corta porción a
    /// porción, justo por encima de la pared venosa. A la derecha de la vena entra el tronco gastrocólico
    /// de Henle y por delante la vena cólica media: son los vasos que se lesionan en este paso, por corte
    /// directo o por arrancamiento cuando la tracción es excesiva.
    /// </summary>
    public class NivelDiseccion : Nivel
    {
        class Celda { public Vector3 p; public bool cortada; public MeshRenderer r; }
        const float T_MIN = 0.20f, T_MAX = 0.85f, SUAVE = 0.45f, V_TIRON = 250f, R_CORTE = 5f;
        static readonly Vector3 C_PL = new Vector3(-54, -9, 0), R_PL = new Vector3(7, 6, 20);
        static readonly Vector3 DIR_TR = new Vector3(-0.8f, 0.6f, 0f);

        Tejido pliegue; Sangrado sangre;
        readonly List<Celda> celdas = new List<Celda>();
        Material mFloja, mTensa, mMira;
        bool sujeto, tensa; Vector3 off;
        float exceso, maxT, enfria, distVaso = 99f;
        readonly float[] enfriaRoma = new float[2];
        int cortadas, sinTraccion, lesiones, riesgo, parenquima, pinzamientos, avulsiones, bruscas, tirones, activaciones;
        string ultimaLesion = "";

        public NivelDiseccion()
        {
            numero = 4; corto = "Disección"; titulo = "Disección cerca de vasos: exposición de la vena mesentérica superior";
            intro = "Una lámina de tejido amarillo cubre la vena mesentérica superior por debajo del cuello del páncreas. Con la mano de tracción toma el pliegue del mesocolon (abajo, cerca de la cámara) y llévalo hacia ti y hacia arriba hasta la zona verde: el tejido se aclara cuando está tenso. Con la otra mano corta cada porción cerrando la tijera SOBRE el tejido, con las hojas paralelas a la vena y nunca apuntando hacia ella. Cuidado con el tronco de Henle (a la izquierda de la imagen) y con la vena cólica media.";
            tiempoPar = 170f; recorridoPar = 120f;
            Centrar(new Vector3(-30, -8, 0), 102f, 40f);
        }

        protected override void Construir()
        {
            rol[DM] = "cerrar = cortar"; rol[NM] = "tracción del mesocolon";
            herramienta[DM] = TipoInstrumento.Tijera; herramienta[NM] = TipoInstrumento.Fenestrada;
            anat = new Anatomia(raiz);
            sangre = new Sangrado(raiz);

            var mP = Geo.Mat(0xdcb45e, 0f, 0.3f);
            var malla = Mallas.Elipsoide(C_PL, R_PL, 24, 14, 0.06f);
            Geo.Obj(malla, mP, raiz, "Mesocolon transverso");
            pliegue = new Tejido("mesocolon", malla, mP, 30f, 30f);
            anat.etiquetas.Add(new Etiqueta("Mesocolon transverso (tracción)", C_PL + new Vector3(0, 7, 0)));

            mFloja = Geo.Mat(0xe6c964, 0f, 0.3f); mTensa = Geo.Mat(0xfbf3cf, 0f, 0.5f); mMira = Geo.Mat(0x6cf08a, 0f, 0.5f);
            for (int i = 0; i < 5; i++)
                for (int j = -1; j <= 1; j++)
                {
                    float x = -16f - 6f * i, z = 4.2f * j;
                    float y = anat.ejePortal.Techo(x, z);
                    if (anat.henle != null) y = Mathf.Max(y, anat.henle.Techo(x, z));
                    y = Mathf.Max(y, anat.vColicaMedia.Techo(x, z));
                    var c = new Celda { p = new Vector3(x, y + 1.6f, z) };
                    // la porción queda siempre a más de 1,7 mm de cualquier pared vascular
                    float dv; int tope = 0;
                    while (tope++ < 40) { anat.VasoCercano(c.p + Vector3.up * 0.4f, out dv); if (dv >= 1.7f) break; c.p.y += 0.25f; }
                    var g = Geo.Sphere(1f, mFloja, raiz, c.p);
                    g.name = "Tejido fibroadiposo"; g.transform.localScale = new Vector3(7.4f, 1.9f, 5.4f);
                    c.r = g.GetComponent<MeshRenderer>();
                    celdas.Add(c);
                }
        }

        static float FPl(Vector3 q)
        {
            Vector3 d = q - C_PL;
            return Mathf.Sqrt(d.x * d.x / (R_PL.x * R_PL.x) + d.y * d.y / (R_PL.y * R_PL.y) + d.z * d.z / (R_PL.z * R_PL.z));
        }

        void Lesion(Estructura v, Vector3 p, string como)
        {
            lesiones++; ultimaLesion = v.nombre;
            sangre.Abrir(p, v.mlMin, v.nombre);
            Avisar("LESIÓN de la " + v.nombre + " (" + como + "): comprime 2 s con una pinza cerrada", true);
        }

        Celda CeldaCercana(Vector3 q, out float dist)
        {
            Celda mejor = null; dist = float.MaxValue;
            foreach (var c in celdas)
            {
                if (c.cortada) continue;
                float d = (c.p - q).magnitude;
                if (d < dist) { dist = d; mejor = c; }
            }
            return mejor;
        }

        void Cortar(Herr t)
        {
            activaciones++;
            float d1, d2;
            var v1 = anat.VasoCercano(t.pos, out d1); var v2 = anat.VasoCercano(t.punta, out d2);
            var v = d1 <= d2 ? v1 : v2; float sd = Mathf.Min(d1, d2);
            if (sd < -0.3f) { Lesion(v, d1 <= d2 ? t.pos : t.punta, "corte"); return; }
            if (anat.pancreas.Dist(t.pos) < -1.5f)
            {
                parenquima++; Avisar("Corte en el parénquima pancreático (riesgo de fístula)", true);
                return;
            }
            float dc; var c = CeldaCercana(t.pos, out dc);
            if (c == null || dc > R_CORTE)
            {
                if (sd < 0.8f) { riesgo++; Avisar("Activación a " + sd.ToString("0.0") + " mm de la " + v.nombre + " sin tejido que cortar", true); }
                return;
            }
            if (!tensa) { sinTraccion++; Avisar("Sin tracción el plano no se ve: tensa el tejido antes de cortar", true); return; }
            c.cortada = true; c.r.gameObject.SetActive(false); cortadas++;
            if (sd < 0.3f) { riesgo++; Avisar("Corte muy cerca de la " + v.nombre + " (" + sd.ToString("0.0") + " mm)", true); }
            else if (cortadas == celdas.Count) Avisar("Vena expuesta. Suelta el mesocolon con suavidad", false);
        }

        protected override void Paso(float dt, Herr[] h)
        {
            enfria -= dt;
            Herr td = h[DM], tn = h[NM];

            // ---- mano de tracción ----
            if (tn.cerro && !sujeto)
            {
                Vector3 q = tn.pos - (pliegue.Mover(tn.pos) - tn.pos);
                if (FPl(q) < 1.4f) { sujeto = true; pliegue.Iniciar(q); off = tn.pos - q; }
                else
                {
                    float dv; var v = anat.VasoCercano(tn.pos, out dv);
                    if (dv < -0.5f) { pinzamientos++; Avisar("Pinzaste la " + v.nombre + ": los vasos no se toman con la pinza", true); }
                    else if (anat.pancreas.Dist(tn.pos) < -1.5f) { pinzamientos++; Avisar("No pinces el páncreas", true); }
                }
            }
            if (tn.abrio && sujeto)
            {
                sujeto = false; float t0 = pliegue.tension; pliegue.Soltar();
                if (t0 > (cortadas == celdas.Count ? SUAVE : 0.5f)) { bruscas++; Avisar("Liberación brusca: afloja antes de soltar", true); }
            }
            if (sujeto)
            {
                if (pliegue.Arrastrar(tn.pos - off))
                {
                    sujeto = false; avulsiones++;
                    var mcv = anat.vasos.Find(e => e.tubo == anat.vColicaMedia);
                    sangre.Abrir(anat.vColicaMedia.p[0], 90f, mcv.nombre);
                    Avisar("AVULSIÓN de la vena cólica media por tracción excesiva: comprime 2 s", true);
                }
                maxT = Mathf.Max(maxT, pliegue.tension);
                if (pliegue.tension > T_MAX) exceso += dt;
                if (tn.rapidez > V_TIRON && enfria <= 0f) { tirones++; enfria = 1f; Avisar("Tirón brusco sobre el mesocolon", true); }
            }
            tensa = sujeto && pliegue.tension >= T_MIN && pliegue.tension <= T_MAX
                    && Vector3.Dot(pliegue.desp.normalized, DIR_TR) > 0.2f;

            // ---- mano que corta ----
            if (td.cerro) Cortar(td);

            // ---- lesión roma: empujar la punta dentro de un vaso ----
            foreach (var t in h)
            {
                enfriaRoma[t.mano] -= dt;
                float dv; var v = anat.VasoCercano(t.punta, out dv);
                if (dv < -2.5f && enfriaRoma[t.mano] <= 0f) { enfriaRoma[t.mano] = 3f; Lesion(v, t.punta, "perforación con la punta"); }
            }
            float dd; anat.VasoCercano(td.pos, out distVaso); anat.VasoCercano(td.punta, out dd);
            distVaso = Mathf.Min(distVaso, dd);

            sangre.Paso(dt, h);
            if (cortadas == celdas.Count && !sujeto && !sangre.Activo) Terminar();
        }

        protected override void Fondo(float dt, Herr[] h)
        {
            pliegue.Paso(dt);
            float dc; var mira = iniciado && !terminado ? CeldaCercana(h[DM].pos, out dc) : null;
            if (mira != null && (mira.p - h[DM].pos).magnitude > R_CORTE) mira = null;
            foreach (var c in celdas)
            {
                if (c.cortada) continue;
                var m = c == mira ? mMira : (tensa ? mTensa : mFloja);
                if (c.r.sharedMaterial != m) c.r.sharedMaterial = m;
            }
        }

        public override void Panel()
        {
            if (!iniciado) { GUILayout.Label(intro, Hud.texto); return; }
            string ins;
            if (sangre.Activo) ins = "SANGRADO — Lleva una pinza al foco rojo, ciérrala encima y mantén 2 s.";
            else if (cortadas == celdas.Count) ins = "Vena expuesta. Afloja la tracción por debajo de " + (SUAVE * 100f).ToString("0") + " % y suelta el mesocolon.";
            else if (!sujeto) ins = "Con la mano " + MANO[NM] + " toma el pliegue del mesocolon y llévalo hacia la cámara y hacia arriba.";
            else if (!tensa) ins = "Ajusta la tracción hasta la zona verde (hacia la cámara y hacia arriba). El tejido se aclara cuando está tenso.";
            else ins = "Tejido tenso. Con la mano " + MANO[DM] + " coloca la tijera abierta sobre una porción (se pone verde) y cierra para cortar. Trabaja de superficial a profundo.";
            GUILayout.Label(ins, Hud.texto);
            GUILayout.Label("Tracción del mesocolon: " + (pliegue.tension * 100f).ToString("0") + " %", Hud.dato);
            if (cortadas == celdas.Count) Hud.Barra(pliegue.tension, 0f, SUAVE); else Hud.Barra(pliegue.tension, T_MIN, T_MAX);
            GUILayout.Label("Tejido disecado: " + cortadas + " / " + celdas.Count, Hud.dato);
            Hud.Barra(cortadas / (float)celdas.Count);
            GUILayout.Label("Tijera → vaso más cercano: " + Mathf.Max(0f, distVaso).ToString("0.0") + " mm", Hud.dato);
            Hud.Barra(Mathf.Clamp01(distVaso / 8f), 0.3f / 8f, 1f);
            if (sangre.Activo) { GUILayout.Label("Compresión", Hud.dato); Hud.Barra(sangre.Progreso); }
            Hud.Dato("Tiempo", tiempo.ToString("0.0") + " s");
            Hud.Dato("Lesiones vasculares", lesiones + (lesiones > 0 ? " (" + ultimaLesion + ")" : ""));
            Hud.Dato("Avulsiones por tracción", avulsiones.ToString());
            Hud.Dato("Pérdida de sangre", sangre.ml.ToString("0") + " ml");
            Hud.Dato("Cortes sin tracción", sinTraccion.ToString());
            Hud.Dato("Activaciones de riesgo", riesgo.ToString());
            Hud.Dato("Tracción excesiva", exceso.ToString("0.0") + " s");
            PanelMensaje();
        }

        protected override void Evaluar()
        {
            // Calificación simplificada: avulsión, lesión vascular, corte en el parénquima, tracción excesiva, movimiento brusco y el error propio del nivel.
            C("Avulsiones por tracción", avulsiones.ToString(), 20f * avulsiones, TEJIDO,
                "La tracción arrancó la vena cólica media. Tracciona lo justo para ver el plano y no más.");
            C("Lesiones vasculares", lesiones + (lesiones > 0 ? " (" + ultimaLesion + ")" : ""), 20f * lesiones, PRECISION,
                "Cortaste o perforaste un vaso. Cierra la tijera solo cuando la porción esté en verde y lejos del vaso; acércate desde arriba, no de frente.");
            C("Cortes en el parénquima", parenquima.ToString(), 10f * parenquima, PRECISION,
                "Cortaste dentro del páncreas: riesgo de fístula. El plano de disección queda por debajo del borde inferior.");
            C("Tiempo con tracción excesiva", exceso.ToString("0.0") + " s", Mathf.Min(14f, 2f * exceso), TEJIDO,
                "La tracción estuvo por encima de la zona verde.");
            C("Movimientos bruscos", (tirones + bruscas).ToString(), 4f * (tirones + bruscas), TEJIDO,
                "Movimientos bruscos con tejido tomado: afloja antes de soltar y mueve despacio.");
            C("Cortes sin tracción", sinTraccion.ToString(), 3f * sinTraccion, BIMANUAL,
                "Intentaste cortar con el tejido flojo. La mano de tracción debe estar en la zona verde ANTES de que la otra corte.");
            C("Tracción máxima", (maxT * 100f).ToString("0") + " %", 0f, TEJIDO, "");
        }
    }
}
