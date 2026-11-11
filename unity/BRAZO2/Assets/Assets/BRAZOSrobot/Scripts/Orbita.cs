using UnityEngine;

namespace BrazosRobot
{
    /// <summary>
    /// Cámara orbital igual a la del visor HTML: ángulo horizontal, ángulo vertical, distancia y
    /// campo de visión, alrededor de un punto de la escena (en mm del espacio de la escena).
    /// Las vistas predefinidas se alcanzan con una transición suave.
    /// </summary>
    public class Orbita
    {
        public struct Vista
        {
            public float th, ph, r, fov; public Vector3? mira;
            public Vista(float th, float ph, float r, float fov, Vector3? mira = null)
            { this.th = th; this.ph = ph; this.r = r; this.fov = fov; this.mira = mira; }
        }

        public Camera cam;
        public Transform espacio;
        public Vector3 objetivo;
        public float th, ph, r, fov, rmin, rmax;
        Vista? destino;

        public Orbita(Camera cam, Transform espacio, Vista v, float rmin, float rmax)
        {
            this.cam = cam; this.espacio = espacio; this.rmin = rmin; this.rmax = rmax;
            th = v.th; ph = v.ph; r = v.r; fov = v.fov;
            if (v.mira.HasValue) objetivo = v.mira.Value;
            Aplicar();
        }

        public void Ir(Vista v)
        {
            float dth = Mathf.Atan2(Mathf.Sin(v.th - th), Mathf.Cos(v.th - th));
            v.th = th + dth;
            destino = v;
        }

        public void Arrastrar(Vector2 d)
        {
            destino = null;
            th -= d.x * 0.008f;
            ph = Mathf.Clamp(ph + d.y * 0.008f, 0.02f, Mathf.PI - 0.02f);
        }

        public void Zoom(float rueda)
        {
            destino = null;
            r = Mathf.Clamp(r * Mathf.Exp(-rueda * 0.12f), rmin, rmax);
        }

        public void Actualizar(float dt)
        {
            if (destino.HasValue)
            {
                var v = destino.Value;
                float k = 1f - Mathf.Exp(-dt * 9f);
                th += (v.th - th) * k; ph += (v.ph - ph) * k; r += (v.r - r) * k; fov += (v.fov - fov) * k;
                float err = Mathf.Abs(v.th - th) + Mathf.Abs(v.ph - ph) + Mathf.Abs(v.r - r) / Mathf.Max(r, 1f);
                if (v.mira.HasValue)
                {
                    objetivo = Vector3.Lerp(objetivo, v.mira.Value, k);
                    err += (objetivo - v.mira.Value).magnitude / Mathf.Max(r, 1f);
                }
                if (err < 0.001f) { th = v.th; ph = v.ph; r = v.r; fov = v.fov; if (v.mira.HasValue) objetivo = v.mira.Value; destino = null; }
            }
            Aplicar();
        }

        void Aplicar()
        {
            float s = Mathf.Sin(ph);
            Vector3 p = objetivo + new Vector3(r * s * Mathf.Cos(th), r * Mathf.Cos(ph), r * s * Mathf.Sin(th));
            cam.transform.position = espacio.TransformPoint(p);
            cam.transform.LookAt(espacio.TransformPoint(objetivo), Vector3.up);
            cam.fieldOfView = fov;
        }
    }
}
