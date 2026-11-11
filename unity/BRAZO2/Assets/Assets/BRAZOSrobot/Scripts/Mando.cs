using UnityEngine;

namespace BrazosRobot
{
    /// <summary>
    /// Mando maestro de 6 ejes + gatillo. Geometría y ejes iguales al visor HTML:
    ///   eje 1 = giro de la base (Y), eje 2 = hombro (Z), eje 3 = codo (Z),
    ///   eje 4 = ele azul (X), eje 5 = ele roja (Y), eje 6 = giro del mango (Z).
    /// Se construye en un espacio "diestro" (como Three.js), en milímetros.
    /// El izquierdo es el espejo lateral del derecho.
    /// </summary>
    public class Mando
    {
        public const float HY = 155f, R4 = 110f, R5 = 100f, RF = 55f;
        /// En la posición cero la ele roja queda perpendicular a la azul: los ejes 4, 5 y 6 son
        /// perpendiculares entre sí y la pistola queda acostada de lado.
        public const float Q5_0 = 0f;

        public Transform espacio, root, g1, g2, g3, g4, g5, g6, trig, gim, hf;
        public bool mirror;

        public Mando(Transform parent, float x, bool mirror, Color lado)
        {
            this.mirror = mirror; espacio = parent;
            root = Geo.Grupo(parent, mirror ? "Mando izquierdo" : "Mando derecho", new Vector3(x, 0, 0));
            // el brazo rosado va hacia atrás (lejos del usuario) y el gimbal queda hacia él
            root.localRotation = Quaternion.AngleAxis(90f, Vector3.up);
            if (mirror) root.localScale = new Vector3(1, 1, -1);

            var dark = Geo.Mat(0x4d5a63, 0.3f, 0.45f);
            var yellow = Geo.Mat(0xe8c62a); var pink = Geo.Mat(0xe86aa6);
            var green = Geo.Mat(0x3fae5a); var blue = Geo.Mat(0x2f6fd6, 0.1f, 0.55f);
            var red = Geo.Mat(0xd83a3a, 0.1f, 0.55f); var grey = Geo.Mat(0x8a949b, 0.2f, 0.5f);

            Geo.CylY(40, 14, Geo.Mat(0x27b8c9), root, new Vector3(0, 7, 0));
            Geo.CylY(46, 4, Geo.Mat(lado), root, new Vector3(0, 1, 0));   // aro de color: derecha / izquierda

            g1 = Geo.Grupo(root, "eje1", Vector3.zero);
            Geo.CylY(15, 65, yellow, g1, new Vector3(0, 32.5f, 0));
            Geo.CylZ(9, 46, dark, g1, new Vector3(0, 65, 0));

            g2 = Geo.Grupo(g1, "eje2", new Vector3(0, 65, 0));
            Geo.Box(new Vector3(24, 174, 18), green, g2, new Vector3(0, 87, 0));
            Geo.CylZ(9, 42, dark, g2, new Vector3(0, 174, 0));

            g3 = Geo.Grupo(g2, "eje3", new Vector3(0, 174, 0));
            Geo.LRod(g3, pink, yellow, new Vector3(200, 0, 0), Vector3.zero, new Vector3(0, HY, 0), RF);
            Geo.CylZ(22, 26, yellow, g3, Vector3.zero);                     // refuerzo del eje 3

            gim = Geo.Grupo(g3, "gimbal", new Vector3(200, HY, 0));
            g4 = Geo.Grupo(gim, "eje4", Vector3.zero);
            Geo.CylX(4, 22, dark, g4, new Vector3(-191, 0, 0));
            Geo.LRod(g4, blue, blue, new Vector3(-182, 0, 0), new Vector3(-182, -R4, 0), new Vector3(0, -R4, 0), RF);

            g5 = Geo.Grupo(g4, "eje5", Vector3.zero);
            Geo.CylY(4, 22, dark, g5, new Vector3(0, -R4 + 9, 0));
            Geo.LRod(g5, red, red, new Vector3(0, -R4 + 18, 0), new Vector3(0, -R4 + 18, R5), new Vector3(0, 0, R5), RF);

            g6 = Geo.Grupo(g5, "eje6", Vector3.zero);
            Geo.CylZ(3.5f, 62, dark, g6, new Vector3(0, 0, R5 - 31));
            Geo.CylZ(7, 80, grey, g6, new Vector3(0, 0, -2));
            Geo.Box(new Vector3(11, 42, 14), grey, g6, new Vector3(0, -22, -30));
            trig = Geo.Grupo(g6, "gatillo", new Vector3(0, -7, -14));
            Geo.Box(new Vector3(6, 20, 4), Geo.Mat(0x35b874), trig, new Vector3(0, -10, 0));

            // marco del mango: su +X apunta a lo largo del mango
            hf = Geo.Grupo(g6, "marco del mango", Vector3.zero);
            hf.localRotation = Quaternion.AngleAxis(-90f, Vector3.up);
            Geo.Sphere(3.2f, Geo.Mat(0x18242c), gim, Vector3.zero);
            Poner(new float[6], 0f);
        }

        static Quaternion AA(float deg, Vector3 eje) { return Quaternion.AngleAxis(deg, eje); }

        /// q en grados (ejes 1..6), g = % de gatillo presionado
        public void Poner(float[] q, float g)
        {
            g1.localRotation = AA(q[0], Vector3.up);
            g2.localRotation = AA(q[1], Vector3.forward);
            g3.localRotation = AA(q[2], Vector3.forward);
            g4.localRotation = AA(q[3], Vector3.right);
            g5.localRotation = AA(q[4] + Q5_0, Vector3.up);
            g6.localRotation = AA(q[5], Vector3.forward);
            trig.localRotation = AA(g / 100f * 0.5f * Mathf.Rad2Deg, Vector3.right);
        }

        /// Cinemática directa: posición del centro del gimbal y orientación del mango,
        /// ambas respecto a la base del mando (producto de transformaciones homogéneas).
        public void Pose(out Vector3 C, out Quaternion Q)
        {
            Matrix4x4 inv = root.worldToLocalMatrix;
            Matrix4x4 rel = inv * gim.localToWorldMatrix;
            C = rel.GetColumn(3);
            rel = inv * hf.localToWorldMatrix;
            Q = QuatDe(rel);
        }

        /// Pose real de la mano en el espacio de los mandos: centro del gimbal, hacia dónde apunta la
        /// pistola (eje del mango) y su "arriba". Incluye el espejo del mando izquierdo.
        public void Mango(out Vector3 pos, out Vector3 eje, out Vector3 arriba)
        {
            pos = espacio.InverseTransformPoint(gim.position);
            eje = espacio.InverseTransformVector(hf.TransformVector(Vector3.right)).normalized;
            arriba = espacio.InverseTransformVector(hf.TransformVector(Vector3.up)).normalized;
        }

        /// Cuaternión de la parte de rotación de una matriz (sin escala).
        public static Quaternion QuatDe(Matrix4x4 m)
        {
            Vector3 f = m.GetColumn(2), u = m.GetColumn(1);
            return Quaternion.LookRotation(f, u);
        }
    }
}
