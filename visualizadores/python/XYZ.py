from vpython import *
import serial

# ============================================
# VISUALIZADOR DE POSICION XYZ - BRAZO 3 DOF
# Base giratoria (yaw) + Hombro (pitch) + Codo (pitch)
# 2 eslabones de 18 cm cada uno
# Con filtro EMA en los 3 ejes
# Lee solo los primeros 3 valores del CSV serial
# (el firmware manda 6, se ignoran los ultimos 3)
# ============================================

PUERTO = 'COM4'
BAUDRATE = 115200

L1 = 18   # cm, primer eslabon (hombro -> codo)
L2 = 18   # cm, segundo eslabon (codo -> punta)

ALPHA = 0.2   # filtro EMA: 0.1 = mas suave/mas delay, 0.5 = mas reactivo/mas ruido

scene = canvas(title="Brazo 3 DOF - Posicion XYZ", width=900, height=600,
               background=color.gray(0.15))
scene.forward = vector(-1, -0.6, -1)

# Ejes de referencia
xaxis = arrow(pos=vector(0,0,0), axis=vector(30,0,0), color=color.red, shaftwidth=0.4)
yaxis = arrow(pos=vector(0,0,0), axis=vector(0,30,0), color=color.green, shaftwidth=0.4)
zaxis = arrow(pos=vector(0,0,0), axis=vector(0,0,30), color=color.blue, shaftwidth=0.4)
label(pos=vector(32,0,0), text='X', box=False, color=color.red)
label(pos=vector(0,32,0), text='Y', box=False, color=color.green)
label(pos=vector(0,0,32), text='Z', box=False, color=color.blue)

# Base fija
base_fija = cylinder(pos=vector(0,-2,0), axis=vector(0,2,0), radius=4, color=color.gray(0.5))

# Eslabones (se recalculan cada frame)
link1 = cylinder(pos=vector(0,0,0), axis=vector(0,L1,0), radius=1.2, color=color.yellow)
link2 = cylinder(pos=vector(0,0,0), axis=vector(0,L2,0), radius=1.0, color=color.orange)
punta = sphere(pos=vector(0,0,0), radius=1.8, color=color.red)

# Texto de posicion
txt_pos = label(pos=vector(0,45,0), text="X=0  Y=0  Z=0", box=False, height=16,
                 color=color.white, xoffset=0)

# Texto de estado de conexion
txt_estado = wtext(text="Estado: iniciando...")
scene.append_to_caption("\n")

# ---------- Angulos crudos (grados), llegan del ESP32 o de los sliders ----------
theta1_deg = 0   # base yaw
theta2_deg = 30  # hombro pitch
theta3_deg = -20 # codo pitch

# ---------- Angulos filtrados (los que realmente mueven el brazo) ----------
theta1_filt = theta1_deg
theta2_filt = theta2_deg
theta3_filt = theta3_deg

def calcular_xyz(t1, t2, t3):
    t1r, t2r, t3r = radians(-t1), radians(-t2), radians(-t3)   # -t1 porque la base estaba al reves, -t2/-t3 invierten hombro y codo

    x1 = L1 * cos(t2r) * cos(t1r)
    y1 = L1 * sin(t2r)
    z1 = L1 * cos(t2r) * sin(t1r)

    t23 = t2r + t3r
    x2 = x1 + L2 * cos(t23) * cos(t1r)
    y2 = y1 + L2 * sin(t23)
    z2 = z1 + L2 * cos(t23) * sin(t1r)

    return vector(x1, y1, z1), vector(x2, y2, z2)

def actualizar_escena():
    p_codo, p_punta = calcular_xyz(theta1_filt, theta2_filt, theta3_filt)

    link1.pos = vector(0, 0, 0)
    link1.axis = p_codo

    link2.pos = p_codo
    link2.axis = p_punta - p_codo

    punta.pos = p_punta

    txt_pos.pos = p_punta + vector(0, 6, 0)
    txt_pos.text = f"X={p_punta.x:.1f}  Y={p_punta.y:.1f}  Z={p_punta.z:.1f} cm"

# ---------- Sliders para probar sin hardware ----------
def set_t1(s):
    global theta1_deg, theta1_filt
    theta1_deg = s.value
    theta1_filt = theta1_deg   # sliders van directo, sin filtro
    lbl_t1.text = f"Base (yaw): {theta1_deg:.0f} grados"
    actualizar_escena()

def set_t2(s):
    global theta2_deg, theta2_filt
    theta2_deg = s.value
    theta2_filt = theta2_deg
    lbl_t2.text = f"Hombro (pitch): {theta2_deg:.0f} grados"
    actualizar_escena()

def set_t3(s):
    global theta3_deg, theta3_filt
    theta3_deg = s.value
    theta3_filt = theta3_deg
    lbl_t3.text = f"Codo (pitch): {theta3_deg:.0f} grados"
    actualizar_escena()

lbl_t1 = wtext(text=f"Base (yaw): {theta1_deg:.0f} grados")
slider(min=-180, max=180, value=theta1_deg, length=300, bind=set_t1)
scene.append_to_caption("\n")
lbl_t2 = wtext(text=f"Hombro (pitch): {theta2_deg:.0f} grados")
slider(min=-90, max=90, value=theta2_deg, length=300, bind=set_t2)
scene.append_to_caption("\n")
lbl_t3 = wtext(text=f"Codo (pitch): {theta3_deg:.0f} grados")
slider(min=-150, max=150, value=theta3_deg, length=300, bind=set_t3)
scene.append_to_caption("\n\n")

actualizar_escena()

# ---------- Conexion serial (no tumba el programa si falla) ----------
ser = None
try:
    ser = serial.Serial(PUERTO, BAUDRATE, timeout=0.05)
    print(f"Conectado a {PUERTO}")
    txt_estado.text = f"Estado: conectado a {PUERTO}"
except serial.SerialException as e:
    print(f"AVISO: no se pudo abrir {PUERTO} ({e})")
    print("La escena sigue funcionando con los sliders manuales.")
    txt_estado.text = f"Estado: SIN conexion ({PUERTO}) - usa los sliders"

contador_frames = 0

# ---------- Loop principal ----------
while True:
    rate(60)

    if ser is not None:
        # Drenar TODO el buffer pendiente y quedarse solo con la ultima linea
        linea_valida = None
        while ser.in_waiting:
            linea_valida = ser.readline().decode(errors='ignore').strip()

        if linea_valida:
            try:
                # El firmware manda 6 valores (CSV), este visualizador solo
                # necesita los primeros 3 (base, hombro, codo). Se ignoran
                # los 3 restantes en vez de fallar al desempacar.
                valores = linea_valida.split(',')

                if len(valores) < 3:
                    raise ValueError(f"se esperaban al menos 3 valores, llegaron {len(valores)}")

                t1, t2, t3 = map(float, valores[:3])
                theta1_deg, theta2_deg, theta3_deg = t1, t2, t3

                # Filtro EMA en los 3 ejes
                theta1_filt = ALPHA * t1 + (1 - ALPHA) * theta1_filt
                theta2_filt = ALPHA * t2 + (1 - ALPHA) * theta2_filt
                theta3_filt = ALPHA * t3 + (1 - ALPHA) * theta3_filt

                actualizar_escena()

                contador_frames += 1
                if contador_frames % 30 == 0:
                    txt_estado.text = f"Estado: OK - crudo: {linea_valida} | filtrado: {theta1_filt:.1f},{theta2_filt:.1f},{theta3_filt:.1f}"
            except ValueError:
                txt_estado.text = f"Estado: dato invalido -> '{linea_valida}'"