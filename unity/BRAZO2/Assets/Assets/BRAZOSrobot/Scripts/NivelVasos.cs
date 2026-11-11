using System.Collections.Generic;
using UnityEngine;

namespace BrazosRobot
{
    /// <summary>
    /// NIVEL 5 — Control de vasos pequeños: ramas pancreáticas de la vena esplénica.
    ///
    /// En la pancreatectomía distal con preservación del bazo y de los vasos esplénicos (técnica de
    /// Kimura) el cuerpo del páncreas se separa de la vena esplénica de medial a lateral, y cada ramita
    /// que entra al páncreas se controla una por una antes de seccionarla.
    /// La mano no dominante levanta el cuerpo con el lazo (nunca pinzando el páncreas) y mantiene las
    /// ramas expuestas; la dominante coloca 2 clips del lado de la vena, 1 del lado del páncreas y corta
    /// entre ellos. Si la tracción es excesiva la rama se arranca de la vena y sangra.
    /// </summary>
    public class NivelVasos : Nivel
    {
        class Clip { public float t; public Vector3 w, fijo; public Transform vis; }
        class Rama
        {
            public Vector3 v, p0, P, ejeCorte; public Transform visA, visB;
            public readonly List<Clip> clips = new List<Clip>();
            public bool cortada, avulsionada; public float tc, lCorte; public Sangrado.Foco foco;
            public bool Resuelta { get { return cortada || (avulsionada && (foco == null || !foco.activo)); } }
        }
        const float T_MIN = 0.35f, T_MAX = 0.92f, SUAVE = 0.45f, V_TIRON = 250f;
        const float L_EXPUESTA = 6f, L_AVULSION = 18f, Z_LAZO = 40f;
        static readonly float[] Z_RAMA = { 27f, 36f, 45f, 54f };

        Tejido pancreas; Sangrado sangre; Transform lazo; Vector3 asa0, cima;
        readonly List<Rama> ramas = new List<Rama>();
        Material mRama, mClip;
        bool sujeto; Vector3 off;
        float exceso, maxT, enfria;
        int clipsUsados, oblicuos, malos, estenosis, ciegos, avulsiones, desgarros, lesionVaso, bruscas, tirones, extras;

        public NivelVasos()
        {
            numero = 5; corto = "Vasos"; titulo = "Control de vasos pequeños: ramas de la vena esplénica";
            intro = "Cuatro ramitas unen la cara posterior del páncreas con la vena esplénica. Con la mano de tracción toma el cabo blanco del lazo y levanta el cuerpo del páncreas hasta la zona verde: las ramas aparecen por debajo. Con la otra mano, en cada rama: 2 clips del lado de la vena, 1 clip del lado del páncreas y corta entre ellos (el aplicador corta solo cuando los tres clips están puestos). Coloca cada clip perpendicular a la rama y a más de 2 mm de la vena.";
            tiempoPar = 200f; recorridoPar = 150f;
            Centrar(new Vector3(3, -3, 40), 102f, 42f);
        }

        static int Cerca(Tubo t, float z)
        {
            int k = 0; float best = float.MaxValue;
            for (int i = 0; i < t.n; i++) { float d = Mathf.Abs(t.p[i].z - z); if (d < best) { best = d; k = i; } }
            return k;
        }

        protected override void Construir()
        {
            rol[DM] = "clips; se cambia sola a tijera cuando la rama está lista"; rol[NM] = "tracción del lazo";
            herramienta[DM] = TipoInstrumento.Clipadora; herramienta[NM] = TipoInstrumento.Cadiere;
            anat = new Anatomia(raiz);
            sangre = new Sangrado(raiz);
            pancreas = new Tejido("páncreas", anat.goPancreas.GetComponent<MeshFilter>().sharedMesh, anat.mPancreas, 28f, 30f);
            var pa = anat.pancreas; int ic = Cerca(pa, Z_LAZO);
            lazo = CrearLazo(pa.p[ic].x, pa.p[ic].y, pa.a[ic], pa.b[ic], Z_LAZO, out asa0);
            cima = pa.p[ic] + pa.up[ic] * pa.b[ic];
            anat.etiquetas.Add(new Etiqueta("Lazo vascular", asa0 + new Vector3(0, 3, 0)));

            mRama = Geo.Mat(0x4a57b8, 0.05f, 0.6f); mClip = Geo.Mat(0xdfe4e8, 0.85f, 0.8f);
            var ve = anat.vEsplenica;
            foreach (float z in Z_RAMA)
            {
                int i = Cerca(pa, z), j = Cerca(ve, z);
                var r = new Rama();
                r.p0 = pa.p[i] - pa.lat[i] * (pa.a[i] * 0.5f) - pa.up[i] * (pa.b[i] * 0.866f);
                r.v = ve.p[j] + ve.up[j] * (ve.b[j] * 0.92f) - ve.lat[j] * (ve.a[j] * 0.3f);
                r.P = r.p0;
                r.visA = Geo.Link(0.9f, mRama, raiz).transform; r.visA.name = "Rama pancreática";
                r.visB = Geo.Link(0.9f, mRama, raiz).transform; r.visB.name = "Rama pancreática (muñón)";
                r.visB.gameObject.SetActive(false);
                ramas.Add(r);
            }
            anat.etiquetas.Add(new Etiqueta("Ramas pancreáticas", ramas[1].v + new Vector3(-4, 1, 0)));
        }

        Vector3 Asa() { return asa0 + pancreas.Mover(cima) - cima; }

        void PonerClip(Rama r, float t, Vector3 dirPinza)
        {
            Vector3 eje = (r.P - r.v).normalized;
            Vector3 w = dirPinza - eje * Vector3.Dot(dirPinza, eje);
            if (w.sqrMagnitude < 1e-4f) w = Vector3.Cross(eje, Vector3.up);
            var c = new Clip { t = t, w = w.normalized };
            c.vis = Geo.Link(0.75f, mClip, raiz).transform; c.vis.name = "Clip";
            r.clips.Add(c); clipsUsados++;
        }

        void Avulsionar(Rama r)
        {
            r.avulsionada = true; avulsiones++;
            int nS = 0; foreach (var c in r.clips) if (c.t < 0.5f) nS++;
            if (nS == 0)
            {
                r.foco = sangre.Abrir(r.v, 60f, "rama de la vena esplénica");
                Avisar("AVULSIÓN de una rama: sangra la vena esplénica. Pon un clip en el muñón o comprime 2 s", true);
            }
            else Avisar("AVULSIÓN de una rama por tracción (ya tenía clip del lado de la vena)", true);
            r.lCorte = (r.P - r.v).magnitude; r.ejeCorte = (r.P - r.v).normalized; r.tc = 0.12f;
            foreach (var c in r.clips) c.fijo = Vector3.Lerp(r.v, r.P, c.t);
        }

        void Accion(Rama r, float tt, Herr t)
        {
            float L = (r.P - r.v).magnitude;
            if (L < L_EXPUESTA) { ciegos++; Avisar("Rama sin exponer: levanta el páncreas con el lazo antes de clipar", true); return; }
            Vector3 eje = (r.P - r.v) / L;
            int nS = 0, nE = 0; float sMax = 0f, eMin = 1f;
            foreach (var c in r.clips) { if (c.t < 0.5f) { nS++; sMax = Mathf.Max(sMax, c.t); } else { nE++; eMin = Mathf.Min(eMin, c.t); } }
            bool listo = nS >= 2 && nE >= 1;
            if (listo && tt > sMax + 0.02f && tt < eMin - 0.02f)
            {
                r.cortada = true; r.tc = tt; r.lCorte = L; r.ejeCorte = eje;
                foreach (var c in r.clips) c.fijo = Vector3.Lerp(r.v, r.P, c.t);
                Avisar("Rama seccionada entre clips", false);
                return;
            }
            if (tt * L < 1.2f) { estenosis++; Avisar("Clip pegado a la vena esplénica: la estrecharía. Deja algo de muñón", true); return; }
            if ((1f - tt) * L < 0.6f) { malos++; Avisar("Clip sobre el páncreas: colócalo en la rama", true); return; }
            foreach (var c in r.clips)
                if (Mathf.Abs(c.t - tt) * L < 0.8f) { malos++; Avisar("Clip encima de otro clip", true); return; }
            bool ladoVena = tt < 0.5f;
            if ((ladoVena && nE > 0 && (eMin - tt) * L < 1.5f) || (!ladoVena && nS > 0 && (tt - sMax) * L < 1.5f))
            { malos++; Avisar("Deja al menos 1,5 mm entre los clips de un lado y del otro para poder cortar", true); return; }
            if (listo) extras++;
            PonerClip(r, tt, t.dir);
            if (Mathf.Abs(Vector3.Dot(t.dir, eje)) > 0.8f) { oblicuos++; Avisar("Clip oblicuo: debe quedar perpendicular al vaso", true); }
            else
            {
                if (ladoVena) nS++; else nE++;
                Avisar(nS >= 2 && nE >= 1 ? "Clips completos: corta entre ellos" : "Clip colocado (vena " + nS + "/2 · páncreas " + nE + "/1)", false);
            }
        }

        protected override void Paso(float dt, Herr[] h)
        {
            enfria -= dt;
            Herr td = h[DM], tn = h[NM];

            // ---- tracción con el lazo ----
            if (tn.cerro && !sujeto)
            {
                if ((tn.pos - Asa()).magnitude < 9f) { sujeto = true; pancreas.Iniciar(cima); off = tn.pos - cima; }
                else if (anat.pancreas.Dist(tn.pos - (pancreas.Mover(tn.pos) - tn.pos)) < -1f)
                { malos++; pancreas.Lesion(); Avisar("LESIÓN: no pinces el páncreas, toma el lazo", true); }
                else
                {
                    float dv; var v = anat.VasoCercano(tn.pos, out dv);
                    if (dv < -0.5f) { lesionVaso++; Avisar("Pinzaste la " + v.nombre, true); }
                }
            }
            if (tn.abrio && sujeto)
            {
                sujeto = false; float t0 = pancreas.tension; pancreas.Soltar();
                bool fin = ramas.TrueForAll(r => r.Resuelta);
                if (t0 > (fin ? SUAVE : 0.5f)) { bruscas++; Avisar("Liberación brusca del páncreas", true); }
            }
            if (sujeto)
            {
                if (pancreas.Arrastrar(tn.pos - off))
                {
                    sujeto = false; desgarros++;
                    Avisar("DESGARRO del páncreas: tracción excesiva del lazo", true);
                    Rama peor = null; float lmax = 0f;
                    foreach (var r in ramas)
                        if (!r.cortada && !r.avulsionada) { float l = (r.P - r.v).magnitude; if (l > lmax) { lmax = l; peor = r; } }
                    if (peor != null) Avulsionar(peor);
                    else avulsiones++;          // sin ramas pendientes, el desgarro cuenta igual como un evento
                }
                maxT = Mathf.Max(maxT, pancreas.tension);
                if (pancreas.tension > T_MAX) exceso += dt;
                if (tn.rapidez > V_TIRON && enfria <= 0f) { tirones++; enfria = 1f; Avisar("Tirón brusco: las ramas se arrancan con facilidad", true); }
            }
            foreach (var r in ramas)
            {
                if (r.cortada || r.avulsionada) continue;
                r.P = pancreas.Mover(r.p0);
                if ((r.P - r.v).magnitude > L_AVULSION) Avulsionar(r);
            }

            // ---- la mano dominante cambia de aplicador de clips a tijera cuando la rama más cercana está lista ----
            {
                bool tijera = false; float dmin2 = 15f;
                foreach (var r in ramas)
                {
                    if (r.cortada || r.avulsionada) continue;
                    float u2; float d2 = Mat3.PuntoSeg(td.pos, r.v, r.P, out u2);
                    if (d2 < dmin2)
                    {
                        dmin2 = d2; int nS = 0, nE = 0;
                        foreach (var c in r.clips) { if (c.t < 0.5f) nS++; else nE++; }
                        tijera = nS >= 2 && nE >= 1;
                    }
                }
                if (cambiarHerramienta != null) cambiarHerramienta(DM, tijera ? TipoInstrumento.Tijera : TipoInstrumento.Clipadora);
            }

            // ---- clips y corte ----
            if (td.cerro)
            {
                Rama mejor = null; float dmin = 4f, tt = 0f;
                foreach (var r in ramas)
                {
                    if (r.cortada || r.avulsionada) continue;
                    float u; float d = Mat3.PuntoSeg(td.pos, r.v, r.P, out u);
                    if (d < dmin) { dmin = d; mejor = r; tt = u; }
                }
                if (mejor != null) Accion(mejor, tt, td);
                else
                {
                    Rama munon = null;
                    foreach (var r in ramas)
                        if (r.avulsionada && r.foco != null && r.foco.activo && (td.pos - r.v).magnitude < 6f) munon = r;
                    float dv; var v = anat.VasoCercano(td.pos, out dv);
                    if (munon != null)
                    {
                        sangre.Cerrar(munon.foco);
                        var c = new Clip { t = 0.06f, w = td.dir, fijo = munon.v + (munon.P - munon.v).normalized * 1.2f };
                        c.vis = Geo.Link(0.75f, mClip, raiz).transform; munon.clips.Add(c); clipsUsados++;
                        Avisar("Muñón controlado con clip", false);
                    }
                    else if (dv < -0.5f) { lesionVaso++; Avisar("Clip sobre la " + v.nombre + ": la ocluye. Clipa solo las ramas", true); }
                    else if (anat.pancreas.Dist(td.pos - (pancreas.Mover(td.pos) - td.pos)) < 0f) { malos++; Avisar("Clip sobre el páncreas", true); }
                }
            }

            sangre.Paso(dt, h);
            if (!sujeto && !sangre.Activo && ramas.TrueForAll(r => r.Resuelta)) Terminar();
        }

        protected override void Fondo(float dt, Herr[] h)
        {
            pancreas.Paso(dt);
            lazo.localPosition = pancreas.Mover(cima) - cima;
            foreach (var r in ramas)
            {
                Vector3 P = pancreas.Mover(r.p0);
                if (!r.cortada && !r.avulsionada)
                {
                    Geo.Place(r.visA, r.v, P, 0.9f);
                    Vector3 eje = (P - r.v).normalized;
                    foreach (var c in r.clips)
                    {
                        Vector3 q = Vector3.Lerp(r.v, P, c.t);
                        Vector3 w = c.w - eje * Vector3.Dot(c.w, eje);
                        w = w.sqrMagnitude > 1e-4f ? w.normalized : Vector3.Cross(eje, Vector3.up).normalized;
                        Geo.Place(c.vis, q - w * 2.3f, q + w * 2.3f, 0.75f);
                    }
                }
                else
                {
                    // dos munones: el de la vena queda fijo y el del páncreas se mueve con él
                    float la = Mathf.Max(0.6f, r.tc * r.lCorte - 0.4f), lb = Mathf.Max(0.6f, (1f - r.tc) * r.lCorte * 0.8f);
                    Geo.Place(r.visA, r.v, r.v + r.ejeCorte * la, 0.9f);
                    Vector3 hacia = (r.v - P).normalized;
                    r.visB.gameObject.SetActive(true);
                    Geo.Place(r.visB, P, P + hacia * lb, 0.9f);
                    foreach (var c in r.clips)
                    {
                        Vector3 q = c.t < r.tc || c.t < 0.1f ? c.fijo : P + hacia * (lb * Mathf.Clamp01((1f - c.t) / Mathf.Max(0.05f, 1f - r.tc)));
                        Vector3 eje = c.t < r.tc ? r.ejeCorte : hacia;
                        Vector3 w = c.w - eje * Vector3.Dot(c.w, eje);
                        w = w.sqrMagnitude > 1e-4f ? w.normalized : Vector3.Cross(eje, Vector3.up).normalized;
                        Geo.Place(c.vis, q - w * 2.3f, q + w * 2.3f, 0.75f);
                    }
                }
            }
        }

        public override void Panel()
        {
            if (!iniciado) { GUILayout.Label(intro, Hud.texto); return; }
            bool fin = ramas.TrueForAll(r => r.Resuelta);
            string ins;
            if (sangre.Activo) ins = "SANGRADO — Pon un clip sobre el muñón que sangra (junto a la vena) o comprime 2 s con una pinza cerrada.";
            else if (fin) ins = "Ramas controladas. Baja el páncreas por debajo de " + (SUAVE * 100f).ToString("0") + " % y suelta el lazo.";
            else if (!sujeto) ins = "Con la mano " + MANO[NM] + " toma el cabo blanco del lazo y levanta el cuerpo del páncreas hacia arriba y adelante.";
            else if (pancreas.tension < T_MIN) ins = "Sigue levantando hasta la zona verde para que las ramas queden expuestas.";
            else ins = "Mantén la tracción. Con la mano " + MANO[DM] + ": abre el aplicador, abraza la rama y cierra. Orden por rama: 2 clips junto a la vena, 1 junto al páncreas, y corta en el medio.";
            GUILayout.Label(ins, Hud.texto);
            GUILayout.Label("Tracción del páncreas (lazo): " + (pancreas.tension * 100f).ToString("0") + " %", Hud.dato);
            if (fin) Hud.Barra(pancreas.tension, 0f, SUAVE); else Hud.Barra(pancreas.tension, T_MIN, T_MAX);
            for (int i = 0; i < ramas.Count; i++)
            {
                var r = ramas[i]; int nS = 0, nE = 0;
                foreach (var c in r.clips) { if (c.t < 0.5f) nS++; else nE++; }
                string est = r.cortada ? "seccionada" : (r.avulsionada ? (r.Resuelta ? "arrancada (controlada)" : "ARRANCADA: sangra")
                    : ((r.P - r.v).magnitude < L_EXPUESTA ? "sin exponer" : (nS >= 2 && nE >= 1 ? "lista para cortar" : "vena " + nS + "/2 · páncreas " + nE + "/1")));
                Hud.Dato("Rama " + (i + 1), est);
            }
            if (sangre.Activo) { GUILayout.Label("Compresión", Hud.dato); Hud.Barra(sangre.Progreso); }
            Hud.Dato("Tiempo", tiempo.ToString("0.0") + " s");
            Hud.Dato("Clips usados", clipsUsados + " (ideal " + (3 * ramas.Count) + ")");
            Hud.Dato("Clips mal colocados", (oblicuos + malos + estenosis + ciegos).ToString());
            Hud.Dato("Avulsiones", avulsiones.ToString());
            Hud.Dato("Pérdida de sangre", sangre.ml.ToString("0") + " ml");
            Hud.Dato("Tracción excesiva", exceso.ToString("0.0") + " s");
            PanelMensaje();
        }

        protected override void Evaluar()
        {
            // Calificación simplificada: desgarro o avulsión, lesión vascular, clip mal colocado, tracción excesiva, movimiento brusco y el error propio del nivel.
            int ok = 0; foreach (var r in ramas) if (r.cortada) ok++;
            C("Ramas controladas y seccionadas", ok + " / " + ramas.Count, 0f, PRECISION, "");
            C("Desgarros y avulsiones", avulsiones.ToString(), 12f * avulsiones, TEJIDO,
                "Arrancaste una rama o desgarraste el páncreas por tracción. Estas venas miden 1–2 mm: levanta lo justo para verlas (zona verde) y no sigas subiendo mientras trabajas.");
            C("Lesiones vasculares", lesionVaso.ToString(), 15f * lesionVaso, PRECISION,
                "Cerraste el aplicador o la pinza sobre la vena o la arteria esplénica. En esta técnica esos vasos se preservan: identifica la rama antes de cerrar.");
            C("Clips mal colocados", (oblicuos + estenosis + malos).ToString(), 4f * (oblicuos + estenosis + malos), PRECISION,
                "Clip oblicuo, pegado a la vena, sobre el páncreas o sin espacio para cortar. Ponlo perpendicular a la rama: dos junto a la vena (sin tocarla), uno junto al páncreas y espacio libre en medio.");
            C("Tiempo con tracción excesiva", exceso.ToString("0.0") + " s", Mathf.Min(14f, 2f * exceso), TEJIDO,
                "Trabajaste con el páncreas por encima de la zona verde.");
            C("Movimientos bruscos", (tirones + bruscas).ToString(), 4f * (tirones + bruscas), TEJIDO,
                "Movimientos bruscos con el páncreas suspendido.");
            C("Intentos sin exponer la rama", ciegos.ToString(), 5f * ciegos, BIMANUAL,
                "Intentaste clipar con la rama floja. La mano del lazo debe sostener la tracción en verde mientras la otra clipa.");
            C("Tracción máxima", (maxT * 100f).ToString("0") + " %", 0f, TEJIDO, "");
        }
    }
}
