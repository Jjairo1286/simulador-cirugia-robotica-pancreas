using System.Collections.Generic;
using UnityEngine;

namespace BrazosRobot
{
    /// <summary>Objeto que las pinzas pueden agarrar.</summary>
    public class Pieza : MonoBehaviour
    {
        public Rigidbody rb;
        public Pinza sujetaPor;
        public bool esAnillo;
        public Vector3 inicioPos;
        public Quaternion inicioRot;
    }

    static class Fis
    {
        public static void SetVel(Rigidbody rb, Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v; rb.angularVelocity = Vector3.zero;
#else
            rb.velocity = v; rb.angularVelocity = Vector3.zero;
#endif
        }
        public static void Damping(Rigidbody rb, float lin, float ang)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = lin; rb.angularDamping = ang;
#else
            rb.drag = lin; rb.angularDrag = ang;
#endif
        }
    }

    /// <summary>
    /// Física de un instrumento: el vástago y las dos mordazas tienen colliders cinemáticos que
    /// empujan los objetos, y la pinza agarra lo que queda entre las mordazas al cerrarse.
    /// Si la otra pinza agarra un objeto que ya está sujeto, se lo pasa (transferencia en el aire).
    /// </summary>
    public class Pinza
    {
        const float CIERRA = 22f, ABRE = 30f;     // apertura (grados) para agarrar / soltar
        const float RADIO_AGARRE = 0.0065f;       // m, zona entre las mordazas (tolerante)

        readonly Instrumento I;
        readonly Transform raiz, sujetador;
        readonly Rigidbody vastago, mordA, mordB;
        readonly CapsuleCollider cVastago;
        public Pieza sujeta;
        public Pinza otra;
        Vector3 prevPos;
        bool abierta = true;   // solo agarra al pasar de abierta a cerrada

        public Pinza(Instrumento inst, Transform raiz, string nombre)
        {
            I = inst; this.raiz = raiz;
            vastago = Proxy(nombre + " vástago", 0.004f, out cVastago);
            CapsuleCollider tmp;
            mordA = Proxy(nombre + " mordaza A", 0.0012f, out tmp);
            mordB = Proxy(nombre + " mordaza B", 0.0012f, out tmp);
            sujetador = new GameObject(nombre + " sujetador").transform;
            sujetador.SetParent(raiz, true);
            Seguir(60f, true);
        }

        Rigidbody Proxy(string n, float r, out CapsuleCollider c)
        {
            var g = new GameObject(n);
            g.transform.SetParent(raiz, true);
            c = g.AddComponent<CapsuleCollider>();
            c.direction = 1; c.radius = r; c.height = 0.01f;
            c.contactOffset = 0.0003f;
            var rb = g.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            return rb;
        }

        static void Mover(Rigidbody rb, CapsuleCollider c, Vector3 a, Vector3 b, float r, bool inmediato)
        {
            Vector3 d = b - a; float L = d.magnitude;
            Quaternion q = Quaternion.FromToRotation(Vector3.up, d / Mathf.Max(L, 1e-6f));
            if (c != null) c.height = L + 2f * r;
            if (inmediato) { rb.transform.SetPositionAndRotation((a + b) * 0.5f, q); }
            else { rb.MovePosition((a + b) * 0.5f); rb.MoveRotation(q); }
        }

        /// Sigue al instrumento (llamar en FixedUpdate). op = apertura en grados.
        public void Seguir(float op, bool inmediato = false)
        {
            Mover(vastago, cVastago, I.pivot.TransformPoint(new Vector3(-30, 0, 0)), I.pitchG.position, 0.004f, inmediato);
            Mover(mordA, null, I.jawA.position, I.jawA.TransformPoint(new Vector3(I.largo, 0, 0)), 0.0012f, inmediato);
            Mover(mordB, null, I.jawB.position, I.jawB.TransformPoint(new Vector3(I.largo, 0, 0)), 0.0012f, inmediato);
            mordA.GetComponent<CapsuleCollider>().height = I.largo * 0.001f + 0.0024f;
            mordB.GetComponent<CapsuleCollider>().height = I.largo * 0.001f + 0.0024f;

            // marco de la punta en coordenadas del mundo (TransformVector conserva el espejo de la escena)
            Vector3 f = I.tipG.TransformVector(Vector3.right).normalized;
            Vector3 u = I.tipG.TransformVector(Vector3.up).normalized;
            Vector3 centro = I.tipG.TransformPoint(new Vector3(I.largo * 0.55f, 0, 0));
            prevPos = sujetador.position;
            sujetador.SetPositionAndRotation(centro, Quaternion.LookRotation(f, u));

            if (op > ABRE)
            {
                abierta = true;
                if (sujeta != null) Soltar();
            }
            else if (op < CIERRA && abierta)
            {
                abierta = false;
                if (sujeta == null) Agarrar(centro);
            }
        }

        void Agarrar(Vector3 centro)
        {
            Pieza mejor = null; float dmin = float.MaxValue;
            foreach (var col in Physics.OverlapSphere(centro, RADIO_AGARRE))
            {
                var p = col.attachedRigidbody != null ? col.attachedRigidbody.GetComponent<Pieza>() : null;
                if (p == null) continue;
                float d = (col.ClosestPoint(centro) - centro).sqrMagnitude;
                if (d < dmin) { dmin = d; mejor = p; }
            }
            if (mejor == null) return;
            if (mejor.sujetaPor != null) mejor.sujetaPor.Liberar();     // transferencia entre pinzas
            sujeta = mejor; mejor.sujetaPor = this;
            mejor.rb.isKinematic = true;
            mejor.rb.interpolation = RigidbodyInterpolation.None;
            mejor.transform.SetParent(sujetador, true);
        }

        /// Suelta sin velocidad (para pasarlo a la otra pinza)
        public void Liberar()
        {
            if (sujeta == null) return;
            sujeta.transform.SetParent(raiz, true);
            sujeta.sujetaPor = null;
            sujeta = null;
        }

        public void Soltar()
        {
            if (sujeta == null) return;
            var p = sujeta;
            Liberar();
            p.rb.isKinematic = false;
            p.rb.interpolation = RigidbodyInterpolation.Interpolate;
            Vector3 v = (sujetador.position - prevPos) / Mathf.Max(Time.fixedDeltaTime, 1e-4f);
            Fis.SetVel(p.rb, Vector3.ClampMagnitude(v, 0.3f));
        }
    }

    /// <summary>
    /// Tablero de transferencia de anillos (como el ejercicio "peg transfer" del FLS):
    /// 6 anillos en los postes de la izquierda que hay que pasar a los postes de la derecha,
    /// idealmente pasándolos de una pinza a la otra en el aire. Incluye cubos sueltos.
    /// Las posiciones se dan en el espacio de la escena (mm) y se convierten a metros del mundo.
    /// </summary>
    public class Practica
    {
        public const float TABLA_Y = -20f;          // superficie del tablero (mm)
        const float POSTE_R = 2f, POSTE_H = 18f;
        const float ANILLO_R = 6.5f, ANILLO_TUBO = 1.5f;

        readonly Transform espacio;
        public Transform raiz;
        public readonly List<Pieza> piezas = new List<Pieza>();
        readonly List<Vector3> origen = new List<Vector3>(), destino = new List<Vector3>();
        public int enDestino;
        public float tiempo; public bool corriendo, terminado;

        public Practica(Transform espacio)
        {
            this.espacio = espacio;
            raiz = new GameObject("Practica (física)").transform;

            // tablero
            var tabla = Bloque("Tablero", new Vector3(15, TABLA_Y - 3, 0), new Vector3(80, 6, 110), Geo.Mat(0x2b3a44, 0.1f, 0.3f));
            tabla.AddComponent<BoxCollider>();
            // tejido de fondo (por si algo cae fuera del tablero)
            Bloque("Tejido", new Vector3(15, -44, 0), new Vector3(200, 8, 220), Geo.Mat(0xb5766f, 0f, 0.2f)).AddComponent<BoxCollider>();

            float[] xs = { -5f, 15f, 35f };
            foreach (float x in xs)
                foreach (float z in new[] { 24f, 38f })
                {
                    origen.Add(new Vector3(x, 0, -z));    // izquierda del usuario
                    destino.Add(new Vector3(x, 0, z));    // derecha del usuario
                }
            var pegM = Geo.Mat(0xe6e9eb, 0.2f, 0.6f);
            var dstM = Geo.Mat(0x35b874, 0.1f, 0.5f);
            foreach (var p in origen) Poste(p, pegM);
            foreach (var p in destino) Poste(p, dstM);

            var ringM = Geo.Mat(0xe0489a, 0.1f, 0.4f);
            for (int i = 0; i < origen.Count; i++) Anillo(origen[i], ringM);
            var cubeM = Geo.Mat(0x3d8fee, 0.1f, 0.4f);
            for (int i = 0; i < 3; i++) Cubo(new Vector3(50, 0, -10 + 10 * i), cubeM);
        }

        Vector3 Mundo(Vector3 mm) { return espacio.TransformPoint(mm); }
        static float M(float mm) { return mm * 0.001f; }

        GameObject Bloque(string n, Vector3 centroMm, Vector3 tamMm, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = n; g.transform.SetParent(raiz, false);
            g.transform.position = Mundo(centroMm);
            g.transform.localScale = tamMm * 0.001f;
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            return g;
        }

        void Poste(Vector3 baseMm, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = "Poste"; g.transform.SetParent(raiz, false);
            g.transform.position = Mundo(baseMm + new Vector3(0, TABLA_Y + POSTE_H / 2f, 0));
            g.transform.localScale = new Vector3(M(2 * POSTE_R), M(POSTE_H / 2f), M(2 * POSTE_R));
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            var c = g.AddComponent<CapsuleCollider>();     // en espacio del cilindro (radio 0.5, alto 2)
            c.direction = 1; c.radius = 0.5f; c.height = 2f;
        }

        Pieza NuevaPieza(string n, Vector3 posMundo, float masa)
        {
            var g = new GameObject(n);
            g.transform.SetParent(raiz, false);
            g.transform.position = posMundo;
            var rb = g.AddComponent<Rigidbody>();
            rb.mass = masa;
            Fis.Damping(rb, 0.6f, 1.5f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = 20;
            var p = g.AddComponent<Pieza>();
            p.rb = rb; p.inicioPos = posMundo; p.inicioRot = Quaternion.identity;
            piezas.Add(p);
            return p;
        }

        void Anillo(Vector3 baseMm, Material m)
        {
            var p = NuevaPieza("Anillo", Mundo(baseMm + new Vector3(0, TABLA_Y + ANILLO_TUBO + 0.2f, 0)), 0.003f);
            p.esAnillo = true;
            var vis = new GameObject("visual"); vis.transform.SetParent(p.transform, false);
            vis.transform.localScale = Vector3.one * 0.001f;
            vis.AddComponent<MeshFilter>().sharedMesh = Geo.Torus(ANILLO_R, ANILLO_TUBO, 40, 14);
            vis.AddComponent<MeshRenderer>().sharedMaterial = m;
            // collider compuesto: 12 cápsulas alrededor (un collider convexo taparía el agujero)
            const int N = 12;
            float seg = 2f * ANILLO_R * Mathf.Sin(Mathf.PI / N);
            for (int i = 0; i < N; i++)
            {
                float a = i * 2f * Mathf.PI / N;
                var s = new GameObject("c" + i); s.transform.SetParent(p.transform, false);
                s.transform.localPosition = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * M(ANILLO_R);
                s.transform.localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)));
                var c = s.AddComponent<CapsuleCollider>();
                c.direction = 2; c.radius = M(ANILLO_TUBO); c.height = M(seg + 2f * ANILLO_TUBO);
                c.contactOffset = 0.0003f;
            }
        }

        void Cubo(Vector3 baseMm, Material m)
        {
            const float L = 7f;
            var p = NuevaPieza("Cubo", Mundo(baseMm + new Vector3(0, TABLA_Y + L / 2f + 0.2f, 0)), 0.004f);
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "visual"; g.transform.SetParent(p.transform, false);
            g.transform.localScale = Vector3.one * M(L);
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            g.GetComponent<BoxCollider>().contactOffset = 0.0003f;
        }

        public void Reiniciar(Pinza a, Pinza b)
        {
            a.Liberar(); b.Liberar();
            foreach (var p in piezas)
            {
                p.transform.SetParent(raiz, true);
                p.rb.isKinematic = false;
                p.transform.SetPositionAndRotation(p.inicioPos, p.inicioRot);
                Fis.SetVel(p.rb, Vector3.zero);
            }
            tiempo = 0; corriendo = false; terminado = false;
        }

        /// Cuenta los anillos apoyados en un poste de destino y lleva el tiempo.
        public void Actualizar(float dt)
        {
            int n = 0; bool alguno = false;
            foreach (var p in piezas)
            {
                if (p.sujetaPor != null) alguno = true;
                if (!p.esAnillo || p.sujetaPor != null) continue;
                Vector3 mm = espacio.InverseTransformPoint(p.transform.position);
                if (mm.y > TABLA_Y + POSTE_H) continue;
                foreach (var d in destino)
                    if (new Vector2(mm.x - d.x, mm.z - d.z).magnitude < ANILLO_R - ANILLO_TUBO) { n++; break; }
            }
            enDestino = n;
            if (!corriendo && !terminado && alguno) corriendo = true;
            if (corriendo) tiempo += dt;
            if (corriendo && n == origen.Count) { corriendo = false; terminado = true; }
        }
    }
}
