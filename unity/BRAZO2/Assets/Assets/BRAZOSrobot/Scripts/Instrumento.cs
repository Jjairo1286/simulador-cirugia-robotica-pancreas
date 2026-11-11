using System.Collections.Generic;
using UnityEngine;

namespace BrazosRobot
{
    /// <summary>
    /// Tipos de instrumento de 8 mm (familia EndoWrist). Largo de mordaza y apertura máxima según el
    /// catálogo de instrumentos da Vinci X/Xi de Intuitive; el Maryland es aproximado.
    /// </summary>
    public enum TipoInstrumento { Cadiere, Fenestrada, Maryland, Tijera, Portaagujas, Clipadora }

    public static class Catalogo
    {
        public static string Nombre(TipoInstrumento t)
        {
            switch (t)
            {
                case TipoInstrumento.Fenestrada: return "Pinza bipolar fenestrada";
                case TipoInstrumento.Maryland: return "Pinza bipolar Maryland (disectora)";
                case TipoInstrumento.Tijera: return "Tijera curva monopolar (Hot Shears)";
                case TipoInstrumento.Portaagujas: return "Portaagujas grande";
                case TipoInstrumento.Clipadora: return "Aplicador de clips Hem-o-lok mediano-grande";
                default: return "Pinza Cadiere";
            }
        }
        /// Largo de la mordaza (mm)
        public static float Largo(TipoInstrumento t)
        {
            switch (t)
            {
                case TipoInstrumento.Fenestrada: return 21f;
                case TipoInstrumento.Maryland: return 18f;
                case TipoInstrumento.Tijera: return 13f;
                case TipoInstrumento.Portaagujas: return 10f;
                case TipoInstrumento.Clipadora: return 21f;
                default: return 20f;
            }
        }
        /// Apertura máxima de las mordazas (grados)
        public static float Apertura(TipoInstrumento t)
        {
            switch (t)
            {
                case TipoInstrumento.Fenestrada: return 45f;
                case TipoInstrumento.Maryland: return 45f;
                case TipoInstrumento.Tijera: return 29f;
                case TipoInstrumento.Portaagujas: return 30f;
                case TipoInstrumento.Clipadora: return 55f;
                default: return 30f;
            }
        }
    }

    /// <summary>Lecturas calculadas de un instrumento (para el panel).</summary>
    public class Lectura
    {
        public float inc, lat, ins;
        public bool muneca, tope;
        public Vector3 punta;
    }

    /// <summary>
    /// Brazo del lado del paciente (estilo Hugo RAS): carro, columna y dos eslabones.
    /// El codo se resuelve con IK de dos eslabones para llegar al extremo del riel.
    /// </summary>
    public class Brazo
    {
        public Vector3 S, E;
        public float L1 = 200f, L2 = 200f;
        Transform l1, l2, jE, jW;

        public Brazo(Transform parent, Color col, float bx, float bz, float topY, float piso)
        {
            var g = Geo.Grupo(parent, "Brazo", Vector3.zero);
            var white = Geo.Mat(0xe9edf0, 0.05f, 0.55f);
            var joint = Geo.Mat(0x3a444b, 0.3f, 0.5f);
            var colM = Geo.Mat(col);
            Geo.Box(new Vector3(240, 70, 240), Geo.Mat(0xd9dee2), g, new Vector3(bx, piso + 35, bz));
            Geo.CylY(29, topY - piso - 70, white, g, new Vector3(bx, (topY + piso + 70) / 2f, bz));
            Geo.CylY(34, 36, colM, g, new Vector3(bx, topY - 30, bz));
            S = new Vector3(bx, topY, bz);
            Geo.Sphere(26, joint, g, S);
            l1 = Geo.Link(16, white, g).transform;
            l2 = Geo.Link(12, white, g).transform;
            jE = Geo.Sphere(20, colM, g, Vector3.zero).transform;
            jW = Geo.Sphere(14, joint, g, Vector3.zero).transform;
        }

        public void Poner(Vector3 P, Vector3 polo)
        {
            Vector3 d = P - S; float dist = d.magnitude; d /= Mathf.Max(dist, 1e-4f);
            float dd = Mathf.Min(dist, L1 + L2 - 0.5f);
            float a = (L1 * L1 - L2 * L2 + dd * dd) / (2f * dd);
            float h = Mathf.Sqrt(Mathf.Max(0f, L1 * L1 - a * a));
            Vector3 pv = (polo - d * Vector3.Dot(polo, d)).normalized;
            E = S + d * a + pv * h;
            Geo.Place(l1, S, E, 16); Geo.Place(l2, E, P, 12);
            jE.localPosition = E; jW.localPosition = P;
        }
    }

    /// <summary>
    /// Instrumento robótico de muñeca (tipo EndoWrist): vástago que pivota sobre el RCM del trócar,
    /// carro que se desliza por un riel de inserción, caja con 4 discos motrices y muñeca de
    /// rotación + cabeceo + guiñada con dos mordazas.
    /// Todo en el espacio diestro de la escena, en milímetros.
    /// </summary>
    public class Instrumento
    {
        public const float INS_MIN = 40f, INS_MAX = 240f;   // recorrido del carro (mm desde el RCM)
        public const float TAU = 0.025f;                    // filtro de temblor (corte ≈ 6 Hz)
        public const float VMAX = 300f;                     // velocidad máxima de la punta (mm/s)
        /// Punto que sigue a la mano: el CENTRO DE LA MUÑECA (TIP = 0). Así, al girar la mano solo se mueve
        /// la muñeca y el vástago no pivota en el trócar (mínimamente invasivo). Con 19 se controlaría el
        /// centro de las mordazas.
        public const float TIP = 0f;
        public const float LIM_MUNECA = 90f;                // muñeca tipo EndoWrist: ±90° de cabeceo y guiñada

        public Vector3 T, Wrest, polo;
        public Transform espacio, pivot, rollG, toolG, pitchG, jawA, jawB, tipG, tip, car;
        public Transform[] discos = new Transform[4];
        public Brazo brazo;

        // estado de teleoperación (todo en el marco del instrumento)
        public Vector3 W, Wc, P, Pa, Wa;
        public Quaternion Q = Quaternion.identity, Qc = Quaternion.identity, Qm = Quaternion.identity, Qo = Quaternion.identity;
        public Quaternion Q0;   // orientación del vástago en reposo: con el mango en cero la muñeca queda recta
        public float roll, pitch, yaw, ins;
        public TipoInstrumento tipo = TipoInstrumento.Cadiere;
        public float largo = 20f, aperturaMax = 30f;
        readonly List<GameObject> piezasTipo = new List<GameObject>();
        Material mAcero, mOscuro;

        public Instrumento(Transform espacio, Color col, Vector3 T, Vector3 Wrest, float cartX, float cartZ, Vector3 polo, float piso)
        {
            this.espacio = espacio; this.T = T; this.Wrest = Wrest; this.polo = polo;
            W = Wc = Wa = Wrest;
            Q0 = Q = Qc = Base(Wrest - T);

            var steel = Geo.Mat(0xb7c1c8, 0.6f, 0.65f);
            var dark = Geo.Mat(0x4d5a63, 0.3f, 0.45f);
            var jawM = Geo.Mat(0xd4dbe0, 0.6f, 0.7f);
            var colM = Geo.Mat(col);

            pivot = Geo.Grupo(espacio, "Instrumento", T);
            Geo.CylX(6.4f, 46, colM, pivot, Vector3.zero);                      // cánula en el trócar
            var anillo = Geo.Obj(Geo.Torus(12, 1.3f), colM, espacio, "RCM");     // centro remoto de movimiento
            anillo.transform.localPosition = T;

            rollG = Geo.Grupo(pivot, "rotacion", Vector3.zero);
            toolG = Geo.Grupo(rollG, "vastago", Vector3.zero);
            Geo.CylX(4, 280, Geo.Mat(0x2c3136, 0.3f, 0.35f), toolG, new Vector3(-140, 0, 0));   // vástago de 8 mm, oscuro y mate como en el robot real
            Geo.CylX(5.2f, 6, dark, toolG, new Vector3(-280, 0, 0));
            Geo.Box(new Vector3(280, 0.3f, 1.0f), Geo.Mat(0x8a6a2a, 0.1f, 0.3f), toolG, new Vector3(-140, 4, 0));   // marca de rotación
            // extremo distal del vástago: anillo metálico, cuello y horquilla redondeada del cabeceo
            mAcero = steel; mOscuro = dark;
            Geo.CylX(4.05f, 2.5f, steel, toolG, new Vector3(-2.5f, 0, 0));
            Geo.Capsula(toolG, new Vector3(-1.5f, 0, 0), new Vector3(1.5f, 0, 0), 3.2f, dark);
            for (int s = -1; s <= 1; s += 2) Geo.Capsula(toolG, new Vector3(1f, 0, s * 2.6f), new Vector3(8f, 0, s * 2.6f), 1.15f, steel);
            Geo.CylZ(1.05f, 7.4f, dark, toolG, new Vector3(8, 0, 0));

            // eslabón de cabeceo con la horquilla de la guiñada (como la muñeca EndoWrist)
            pitchG = Geo.Grupo(toolG, "cabeceo", new Vector3(8, 0, 0));
            Geo.Capsula(pitchG, new Vector3(0.5f, 0, 0), new Vector3(6f, 0, 0), 2.1f, dark);
            for (int s = -1; s <= 1; s += 2) Geo.Capsula(pitchG, new Vector3(5f, s * 2.2f, 0), new Vector3(11f, s * 2.2f, 0), 0.95f, steel);
            Geo.CylY(0.8f, 6.2f, dark, pitchG, new Vector3(11, 0, 0));
            jawA = Geo.Grupo(pitchG, "mordaza", new Vector3(11, 0, 0));
            jawB = Geo.Grupo(pitchG, "mordaza", new Vector3(11, 0, 0));
            tipG = Geo.Grupo(pitchG, "guinada", new Vector3(11, 0, 0));
            tip = Geo.Grupo(tipG, "punta", new Vector3(16, 0, 0));
            PonerTipo(TipoInstrumento.Cadiere);

            // riel de inserción, soporte de la cánula, carro y caja con 4 discos motrices
            Spar(pivot, col, out car, discos);
            brazo = new Brazo(espacio, col, cartX, cartZ, 440, piso);
            Paso(1f, 1f, 0f);
        }

        /// Cambia la punta del instrumento (forma y largo de las mordazas, apertura máxima).
        public void PonerTipo(TipoInstrumento t)
        {
            tipo = t; largo = Catalogo.Largo(t); aperturaMax = Catalogo.Apertura(t);
            foreach (var g in piezasTipo) Util.Destruir(g);
            piezasTipo.Clear();
            var acero = Geo.Mat(0xd3d9de, 0.75f, 0.75f);
            var negro = Geo.Mat(0x1e2226, 0.2f, 0.4f);
            for (int k = 0; k < 2; k++)
            {
                float side = k == 0 ? -1f : 1f; Transform jaw = k == 0 ? jawA : jawB;
                float L = largo, hB = 3.2f, hT = 2.4f, wB = 1.6f, wT = 1.2f, curva = 0f, aguda = 0f;
                switch (t)
                {
                    case TipoInstrumento.Fenestrada: hB = 3.1f; hT = 2.1f; wB = 1.55f; wT = 1.0f; curva = 0.7f; aguda = 0.15f; break;
                    case TipoInstrumento.Maryland: hB = 2.8f; hT = 0.9f; wB = 1.45f; wT = 0.6f; curva = 3.4f; aguda = 0.7f; break;
                    case TipoInstrumento.Tijera: hB = 2.9f; hT = 0.25f; wB = 0.6f; wT = 0.35f; curva = 2.2f; aguda = 1f; break;
                    case TipoInstrumento.Portaagujas: hB = 3.3f; hT = 2.7f; wB = 1.9f; wT = 1.6f; curva = 0f; aguda = 0.05f; break;
                    case TipoInstrumento.Clipadora: hB = 2.5f; hT = 2.1f; wB = 1.35f; wT = 1.1f; curva = 0f; aguda = 0f; break;
                }
                var m = Geo.Obj(Geo.Mandibula(L, hB, hT, wB, wT, curva, aguda, side), acero, jaw, "mordaza " + Catalogo.Nombre(t));
                piezasTipo.Add(m);
                piezasTipo.Add(Geo.CylY(2.1f, 1.4f, acero, jaw, new Vector3(0, side * 0.75f, 0)));
                // detalles: ventana de las pinzas fenestradas, inserto del portaagujas, canal del clip
                if (t == TipoInstrumento.Cadiere || t == TipoInstrumento.Fenestrada)
                    piezasTipo.Add(Geo.Box(new Vector3(L * 0.45f, hB * 0.38f, 0.25f), negro, jaw, new Vector3(L * 0.52f, curva * 0.27f, side * (wB * 0.98f))));
                if (t == TipoInstrumento.Portaagujas)
                    piezasTipo.Add(Geo.Box(new Vector3(L * 0.75f, hB * 0.7f, 0.2f), Geo.Mat(0xb8962e, 0.8f, 0.6f), jaw, new Vector3(L * 0.52f, 0, side * 0.08f)));
                if (t == TipoInstrumento.Clipadora)
                    piezasTipo.Add(Geo.Box(new Vector3(L * 0.8f, 0.5f, 0.25f), negro, jaw, new Vector3(L * 0.55f, 0, side * 0.1f)));
            }
            if (t == TipoInstrumento.Tijera || t == TipoInstrumento.Fenestrada || t == TipoInstrumento.Maryland)
            {
                // funda aislante negra sobre la muñeca de los instrumentos con energía
                var bota = Geo.Capsula(pitchG, new Vector3(-6f, 0, 0), new Vector3(5f, 0, 0), 3.4f, negro);
                piezasTipo.Add(bota);
            }
            tip.localPosition = new Vector3(largo, 0, 0);
        }

        public static readonly int[] DISC_COL = { 0xe39a1f, 0xe0489a, 0x3d8fee, 0x35b874 };

        public static void Spar(Transform pivot, Color col, out Transform car, Transform[] discos)
        {
            var rail = Geo.Mat(0x59656d, 0.4f, 0.5f);
            var joint = Geo.Mat(0x3a444b, 0.3f, 0.5f);
            var colM = Geo.Mat(col);
            Geo.Box(new Vector3(310, 10, 24), rail, pivot, new Vector3(-185, 46, 0));
            Geo.Box(new Vector3(12, 40, 22), rail, pivot, new Vector3(-30, 26, 0));
            Geo.CylX(10, 14, colM, pivot, new Vector3(-30, 0, 0));
            car = Geo.Grupo(pivot, "carro", Vector3.zero);
            Geo.Box(new Vector3(56, 20, 30), joint, car, new Vector3(0, 31, 0));
            Geo.Box(new Vector3(76, 30, 44), Geo.Mat(0xf4f6f7, 0.05f, 0.6f), car, new Vector3(0, 6, 0));
            Geo.Box(new Vector3(76.4f, 4, 44.4f), colM, car, new Vector3(0, -7, 0));
            for (int i = 0; i < 4; i++)
            {
                var d = Geo.Grupo(car, "disco" + (i + 1), new Vector3(-27 + 18 * i, 6, 22.4f));
                Geo.CylZ(7.5f, 1.6f, joint, d, Vector3.zero);
                Geo.Box(new Vector3(1.8f, 7, 1), Geo.Mat(DISC_COL[i]), d, new Vector3(0, 3.5f, 1));
                if (discos != null) discos[i] = d;
            }
        }

        /// Base ortonormal con +X a lo largo de dir y +Y lo más vertical posible.
        public static Quaternion Base(Vector3 dir)
        {
            Vector3 vx = dir.normalized;
            Vector3 vz = Vector3.Cross(vx, Vector3.up).normalized;
            Vector3 vy = Vector3.Cross(vz, vx);
            return Quaternion.LookRotation(vz, vy);
        }

        /// Esclavo: filtro de temblor + límite de velocidad, RCM, IK de la muñeca y mordazas.
        /// a = fracción del filtro para este paso; op = apertura de la pinza en grados.
        public void Paso(float a, float dt, float op, Lectura k = null)
        {
            Vector3 step = (Wc - W) * a;
            float mx = VMAX * dt;
            if (step.magnitude > mx) step = step.normalized * mx;
            W += step;
            Q = Quaternion.Slerp(Q, Qc, a);

            // la orden es el centro de la muñeca (si TIP > 0, un punto TIP mm por delante de ella)
            Vector3 muneca = W - Q * new Vector3(TIP, 0, 0);
            // RCM: el vástago pasa siempre por el trócar
            Vector3 vx = muneca - T;
            float d = vx.magnitude; vx /= Mathf.Max(d, 1e-4f);
            ins = Mathf.Clamp(d, INS_MIN, INS_MAX);
            Quaternion Qs = Base(vx);
            pivot.localRotation = Qs;
            toolG.localPosition = new Vector3(ins - 8f, 0, 0);
            car.localPosition = new Vector3(ins - 318f, 0, 0);

            // IK de la muñeca: R = R_vástago⁻¹ · R_mango = Rx(rotación) · Rz(cabeceo) · Ry(guiñada)
            Matrix4x4 M = Matrix4x4.Rotate(Quaternion.Inverse(Qs) * Q);
            float b = -Mathf.Asin(Mathf.Clamp(M.m01, -1f, 1f)) * Mathf.Rad2Deg;
            float r = Mathf.Atan2(M.m21, M.m11) * Mathf.Rad2Deg;
            float c = Mathf.Atan2(M.m02, M.m00) * Mathf.Rad2Deg;
            while (r - roll > 180f) r -= 360f;
            while (r - roll < -180f) r += 360f;
            if (r > 270f) r -= 360f; if (r < -270f) r += 360f;
            roll = r; pitch = Mathf.Clamp(b, -LIM_MUNECA, LIM_MUNECA); yaw = Mathf.Clamp(c, -LIM_MUNECA, LIM_MUNECA);

            rollG.localRotation = Quaternion.AngleAxis(roll, Vector3.right);
            pitchG.localRotation = Quaternion.AngleAxis(pitch, Vector3.forward);
            jawA.localRotation = Quaternion.AngleAxis(yaw + op / 2f, Vector3.up);
            jawB.localRotation = Quaternion.AngleAxis(yaw - op / 2f, Vector3.up);
            tipG.localRotation = Quaternion.AngleAxis(yaw, Vector3.up);
            float[] vals = { roll, pitch, yaw + op / 2f, yaw - op / 2f };
            for (int i = 0; i < 4; i++) discos[i].localRotation = Quaternion.AngleAxis(vals[i], Vector3.forward);

            // el brazo sigue al extremo trasero del riel
            Vector3 Aw = espacio.InverseTransformPoint(pivot.TransformPoint(new Vector3(-330, 46, 0)));
            brazo.Poner(Aw, polo);

            if (k != null)
            {
                k.tope = !Mathf.Approximately(d, ins);
                k.muneca = Mathf.Abs(b) > LIM_MUNECA || Mathf.Abs(c) > LIM_MUNECA;
                k.ins = ins;
                k.inc = Mathf.Asin(Mathf.Clamp(-vx.y, -1f, 1f)) * Mathf.Rad2Deg;
                k.lat = Mathf.Atan2(vx.z, vx.x) * Mathf.Rad2Deg;
                k.punta = espacio.InverseTransformPoint(tip.position);
            }
        }

        /// Endoscopio fijo en el puerto central, con su brazo.
        public static void Endoscopio(Transform espacio, Vector3 TE, Vector3 mira, float piso)
        {
            var col = Geo.Hex(0x2f8fc9);
            var pg = Geo.Grupo(espacio, "Endoscopio", TE);
            pg.localRotation = Base(mira - TE);
            Geo.CylX(6.4f, 46, Geo.Mat(col), pg, Vector3.zero);
            Geo.CylX(5, 300, Geo.Mat(0xb7c1c8, 0.6f, 0.65f), pg, new Vector3(26 - 150, 0, 0));
            Geo.Obj(Geo.Torus(12, 1.3f), Geo.Mat(col), espacio, "RCM").transform.localPosition = TE;
            Transform car;
            Spar(pg, col, out car, null);
            car.localPosition = new Vector3(26 - 300 - 30, 0, 0);
            var b = new Brazo(espacio, col, -600, 0, 470, piso);
            b.Poner(espacio.InverseTransformPoint(pg.TransformPoint(new Vector3(-330, 46, 0))), new Vector3(-0.2f, 1, 0));
        }
    }
}
