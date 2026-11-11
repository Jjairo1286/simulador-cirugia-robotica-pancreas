from vpython import *
import serial
import math

# ============================================================
# CONFIGURACION DE PANTALLA COMPLETA
# ============================================================

# ============================================================
# DIMENSIONES DEL BRAZO (DEFINIDAS PRIMERO)
# ============================================================

L1 = 18.0
L2 = 18.0
ALTURA_GYMBALL = 6.0

# ============================================================
# DIMENSIONES DEL GYMBALL
# ============================================================

RADIO_EXT = 3.2
RADIO_MED = 2.4
RADIO_INT = 1.6
GROSOR = 0.08

# ============================================================
# DIMENSIONES DE LA PINZA
# ============================================================

LARGO_VASTAGO = 5.5
RADIO_VASTAGO = 0.10
LARGO_MORDAZA = 1.0
ANGULO_APERTURA = 18

# ============================================================
# CREAR ESCENA (DESPUES DE LAS DEFINICIONES)
# ============================================================

scene = canvas(
    title="Brazo 3 DOF + GYMBALL 3 DOF - 6 Encoders",
    width=1920,
    height=1080,
    background=color.gray(0.15),
    fullscreen=True
)

scene.forward = vector(-1, -0.6, -1)
scene.center = vector(0, L1, 0)

# ============================================================
# CONFIGURACION SERIAL
# ============================================================

PUERTO = "COM4"
BAUDRATE = 115200
ALPHA = 0.20

# ============================================================
# PANEL DE INFORMACION EN ESQUINA
# ============================================================

panel = wtext(
    text="""
    <div style='
        position: fixed;
        bottom: 20px;
        left: 20px;
        background: rgba(0,0,0,0.75);
        color: #00ff88;
        font-family: monospace;
        font-size: 14px;
        padding: 15px 20px;
        border-radius: 10px;
        border: 1px solid #00ff88;
        z-index: 1000;
        min-width: 280px;
        line-height: 1.6;
        user-select: none;
        pointer-events: none;
    '>
        <b style='color: #ffaa00;'>BRAZO</b><br>
        Base:   <span id='ch0'>0.0</span>°<br>
        Hombro: <span id='ch1'>0.0</span>°<br>
        Codo:   <span id='ch2'>0.0</span>°<br>
        <span style='color:#ffaa00;'>XYZ:</span> 
        <span id='posx'>0.0</span>, 
        <span id='posy'>0.0</span>, 
        <span id='posz'>0.0</span> cm<br>
        <br>
        <b style='color: #66ccff;'>GYMBALL</b><br>
        Roll:  <span id='ch3'>0.0</span>°<br>
        Pitch: <span id='ch4'>0.0</span>°<br>
        Yaw:   <span id='ch5'>0.0</span>°<br>
        <br>
        <span style='color: #888;'>Estado: <span id='estado'>Iniciando...</span></span>
    </div>
    """
)

# ============================================================
# EJES DE REFERENCIA
# ============================================================

xaxis = arrow(pos=vector(0,0,0), axis=vector(30,0,0), color=color.red, shaftwidth=0.25)
yaxis = arrow(pos=vector(0,0,0), axis=vector(0,30,0), color=color.green, shaftwidth=0.25)
zaxis = arrow(pos=vector(0,0,0), axis=vector(0,0,30), color=color.blue, shaftwidth=0.25)

label_x = label(pos=vector(32,0,0), text="X", box=False, color=color.red)
label_y = label(pos=vector(0,32,0), text="Y", box=False, color=color.green)
label_z = label(pos=vector(0,0,32), text="Z", box=False, color=color.blue)

# ============================================================
# BASE FIJA
# ============================================================

base_fija = cylinder(pos=vector(0,-2,0), axis=vector(0,2,0), radius=4, color=color.gray(0.5))

# ============================================================
# BRAZO - ESLABONES
# ============================================================

link1 = cylinder(pos=vector(0,0,0), axis=vector(0,L1,0), radius=1.2, color=color.yellow)
link2 = cylinder(pos=vector(0,L1,0), axis=vector(0,L2,0), radius=1.0, color=color.orange)
codo = sphere(pos=vector(0,L1,0), radius=1.3, color=color.gray(0.6))
punta_brazo = sphere(pos=vector(0,L1+L2,0), radius=1.1, color=color.gray(0.65))

# ============================================================
# GYMBALL
# ============================================================

anillo_yaw = ring(
    pos=vector(0, L1+L2+ALTURA_GYMBALL, 0),
    axis=vector(0,1,0),
    radius=RADIO_EXT,
    thickness=GROSOR,
    color=color.orange
)

anillo_pitch = ring(
    pos=vector(0, L1+L2+ALTURA_GYMBALL, 0),
    axis=vector(1,0,0),
    radius=RADIO_MED,
    thickness=GROSOR,
    color=color.cyan
)

anillo_roll = ring(
    pos=vector(0, L1+L2+ALTURA_GYMBALL, 0),
    axis=vector(0,0,1),
    radius=RADIO_INT,
    thickness=GROSOR,
    color=color.magenta
)

centro_gimbal = sphere(
    pos=vector(0, L1+L2+ALTURA_GYMBALL, 0),
    radius=0.25,
    color=color.gray(0.6)
)

# ============================================================
# MARCADORES VISUALES
# ============================================================

marca_yaw = arrow(
    pos=vector(RADIO_EXT, L1+L2+ALTURA_GYMBALL, 0),
    axis=vector(0.7,0,0),
    shaftwidth=0.12,
    color=color.orange
)

marca_pitch = arrow(
    pos=vector(RADIO_MED, L1+L2+ALTURA_GYMBALL, 0),
    axis=vector(0,0.7,0),
    shaftwidth=0.12,
    color=color.cyan
)

marca_roll = arrow(
    pos=vector(RADIO_INT, L1+L2+ALTURA_GYMBALL, 0),
    axis=vector(0,0,0.7),
    shaftwidth=0.12,
    color=color.magenta
)

# ============================================================
# PINZA LAPAROSCOPICA
# ============================================================

_ang = radians(ANGULO_APERTURA)

_vastago = cylinder(
    pos=vector(0,0,-LARGO_VASTAGO),
    axis=vector(0,0,LARGO_VASTAGO),
    radius=RADIO_VASTAGO,
    color=color.gray(0.75)
)

_pin = sphere(pos=vector(0,0,0), radius=RADIO_VASTAGO*1.5, color=color.gray(0.4))

_dir_mordaza_der = vector(math.sin(_ang), 0, math.cos(_ang)) * LARGO_MORDAZA
_mordaza_der = box(
    pos=_dir_mordaza_der/2,
    axis=_dir_mordaza_der,
    size=vector(_dir_mordaza_der.mag, 0.12, 0.18),
    color=color.white
)

_dir_mordaza_izq = vector(-math.sin(_ang), 0, math.cos(_ang)) * LARGO_MORDAZA
_mordaza_izq = box(
    pos=_dir_mordaza_izq/2,
    axis=_dir_mordaza_izq,
    size=vector(_dir_mordaza_izq.mag, 0.12, 0.18),
    color=color.white
)

_diente_der = sphere(pos=_dir_mordaza_der, radius=0.08, color=color.white)
_diente_izq = sphere(pos=_dir_mordaza_izq, radius=0.08, color=color.white)

# Punto medio de la pinza a lo largo de su eje (z local), para que el
# giro (pos/rotate) se haga desde el centro y no desde la punta del vastago
_extremo_trasero = -LARGO_VASTAGO
_extremo_delantero = LARGO_MORDAZA * math.cos(_ang)
_punto_medio_pinza = (_extremo_trasero + _extremo_delantero) / 2

pinza = compound(
    [_vastago, _pin, _mordaza_der, _mordaza_izq, _diente_der, _diente_izq],
    pos=vector(0,0,0),
    origin=vector(0,0,_punto_medio_pinza)
)

LONGITUD_PINZA = pinza.axis.mag if pinza.axis.mag > 0 else 1.0

# ============================================================
# GRUPOS DE OBJETOS PARA CONTROLAR VISIBILIDAD
# ============================================================

# Todo lo que NO es la pinza (brazo, gymball, ejes, base, marcadores, etiquetas)
objetos_no_pinza = [
    xaxis, yaxis, zaxis,
    label_x, label_y, label_z,
    base_fija,
    link1, link2, codo, punta_brazo,
    anillo_yaw, anillo_pitch, anillo_roll, centro_gimbal,
    marca_yaw, marca_pitch, marca_roll,
]

elementos_visibles = True      # controla ejes/base (boton "Ocultar ejes")
modo_solo_pinza = False        # controla el modo "solo pinza"

# ============================================================
# BOTON PARA OCULTAR/MOSTRAR EJES Y BASE
# ============================================================

def toggle_visibilidad():
    global elementos_visibles
    elementos_visibles = not elementos_visibles

    xaxis.visible = elementos_visibles
    yaxis.visible = elementos_visibles
    zaxis.visible = elementos_visibles
    base_fija.visible = elementos_visibles
    label_x.visible = elementos_visibles
    label_y.visible = elementos_visibles
    label_z.visible = elementos_visibles

    btn_toggle.text = "Mostrar ejes" if not elementos_visibles else "Ocultar ejes"

btn_toggle = button(
    text="Ocultar ejes",
    pos=scene.title_anchor,
    bind=lambda: toggle_visibilidad()
)

# ============================================================
# BOTON PARA MODO "SOLO PINZA" (Y VICEVERSA)
# ============================================================

def toggle_solo_pinza():
    global modo_solo_pinza
    modo_solo_pinza = not modo_solo_pinza

    for obj in objetos_no_pinza:
        obj.visible = not modo_solo_pinza

    # Si volvemos a mostrar todo, respetar el estado del boton de ejes/base
    if not modo_solo_pinza:
        xaxis.visible = elementos_visibles
        yaxis.visible = elementos_visibles
        zaxis.visible = elementos_visibles
        base_fija.visible = elementos_visibles
        label_x.visible = elementos_visibles
        label_y.visible = elementos_visibles
        label_z.visible = elementos_visibles

    btn_solo_pinza.text = "Mostrar todo" if modo_solo_pinza else "Solo pinza"

btn_solo_pinza = button(
    text="Solo pinza",
    pos=scene.title_anchor,
    bind=lambda: toggle_solo_pinza()
)

# ============================================================
# ESTADO DE LOS ENCODERS
# ============================================================

theta1 = 0.0
theta2 = 30.0
theta3 = -20.0
roll = 0.0
pitch = 0.0
yaw = 0.0

theta1_f = theta1
theta2_f = theta2
theta3_f = theta3
roll_f = roll
pitch_f = pitch
yaw_f = yaw

# ============================================================
# CALCULAR XYZ DEL BRAZO
# ============================================================

def calcular_xyz(t1, t2, t3):
    t1r = radians(-t1)
    t2r = radians(t2)
    t3r = radians(t3)
    
    x1 = L1 * cos(t2r) * cos(t1r)
    y1 = L1 * sin(t2r)
    z1 = L1 * cos(t2r) * sin(t1r)
    
    t23 = t2r + t3r
    x2 = x1 + L2 * cos(t23) * cos(t1r)
    y2 = y1 + L2 * sin(t23)
    z2 = z1 + L2 * cos(t23) * sin(t1r)
    
    return vector(x1, y1, z1), vector(x2, y2, z2)

# ============================================================
# ACTUALIZAR GYMBALL
# ============================================================

def actualizar_gimbal(roll_deg, pitch_deg, yaw_deg, posicion):
    posicion_gimbal = posicion + vector(0, ALTURA_GYMBALL, 0)
    
    anillo_yaw.pos = posicion_gimbal
    anillo_pitch.pos = posicion_gimbal
    anillo_roll.pos = posicion_gimbal
    centro_gimbal.pos = posicion_gimbal
    
    eje_yaw = vector(0,1,0)
    eje_pitch = vector(1,0,0)
    eje_roll = vector(0,0,1)
    
    anillo_yaw.axis = eje_yaw
    anillo_pitch.axis = eje_pitch
    anillo_pitch.up = vector(0,1,0)
    anillo_roll.axis = eje_roll
    anillo_roll.up = vector(0,1,0)
    
    ang_yaw = radians(yaw_deg)
    ang_pitch = radians(pitch_deg)
    ang_roll = radians(roll_deg)
    
    marca_yaw_local = vector(RADIO_EXT,0,0).rotate(angle=ang_yaw, axis=eje_yaw)
    marca_yaw.pos = posicion_gimbal + marca_yaw_local
    marca_yaw.axis = vector(0.7,0,0).rotate(angle=ang_yaw, axis=eje_yaw)
    
    marca_pitch_local = vector(RADIO_MED,0,0).rotate(angle=ang_pitch, axis=eje_pitch)
    marca_pitch.pos = posicion_gimbal + marca_pitch_local
    marca_pitch.axis = vector(0,0.7,0).rotate(angle=ang_pitch, axis=eje_pitch)
    
    marca_roll_local = vector(RADIO_INT,0,0).rotate(angle=ang_roll, axis=eje_roll)
    marca_roll.pos = posicion_gimbal + marca_roll_local
    marca_roll.axis = vector(0,0,0.7).rotate(angle=ang_roll, axis=eje_roll)
    
    forward = vector(0,0,1)
    up = vector(0,1,0)
    right = vector(1,0,0)
    
    forward = forward.rotate(angle=ang_yaw, axis=eje_yaw)
    up = up.rotate(angle=ang_yaw, axis=eje_yaw)
    right = right.rotate(angle=ang_yaw, axis=eje_yaw)
    
    forward = forward.rotate(angle=ang_pitch, axis=right)
    up = up.rotate(angle=ang_pitch, axis=right)
    
    up = up.rotate(angle=ang_roll, axis=forward)
    right = right.rotate(angle=ang_roll, axis=forward)
    
    pinza.pos = posicion_gimbal
    pinza.axis = norm(forward) * LONGITUD_PINZA
    pinza.up = norm(up)

# ============================================================
# ACTUALIZAR PANEL DE INFORMACION
# ============================================================

def actualizar_panel(p_punta):
    # Actualizar el panel con los nuevos valores
    panel.text = f"""
    <div style='
        position: fixed;
        bottom: 20px;
        left: 20px;
        background: rgba(0,0,0,0.75);
        color: #00ff88;
        font-family: monospace;
        font-size: 14px;
        padding: 15px 20px;
        border-radius: 10px;
        border: 1px solid #00ff88;
        z-index: 1000;
        min-width: 280px;
        line-height: 1.6;
        user-select: none;
        pointer-events: none;
    '>
        <b style='color: #ffaa00;'>BRAZO</b><br>
        Base:   {theta1_f:.1f}°<br>
        Hombro: {theta2_f:.1f}°<br>
        Codo:   {theta3_f:.1f}°<br>
        <span style='color:#ffaa00;'>XYZ:</span> 
        {p_punta.x:.1f}, {p_punta.y:.1f}, {p_punta.z:.1f} cm<br>
        <br>
        <b style='color: #66ccff;'>GYMBALL</b><br>
        Roll:  {roll_f:.1f}°<br>
        Pitch: {pitch_f:.1f}°<br>
        Yaw:   {yaw_f:.1f}°<br>
        <br>
        <span style='color: #888;'>Estado: OK</span>
    </div>
    """

# ============================================================
# ACTUALIZAR ESCENA COMPLETA
# ============================================================

def actualizar_escena():
    p_codo, p_punta = calcular_xyz(theta1_f, theta2_f, theta3_f)
    
    link1.pos = vector(0,0,0)
    link1.axis = p_codo
    codo.pos = p_codo
    link2.pos = p_codo
    link2.axis = p_punta - p_codo
    punta_brazo.pos = p_punta
    
    actualizar_gimbal(roll_f, pitch_f, yaw_f, p_punta)
    actualizar_panel(p_punta)

# ============================================================
# INICIALIZAR
# ============================================================

actualizar_escena()

# ============================================================
# CONEXION SERIAL
# ============================================================

ser = None
try:
    ser = serial.Serial(PUERTO, BAUDRATE, timeout=0.05)
    print(f"Conectado a {PUERTO}")
except serial.SerialException as e:
    print(f"AVISO: no se pudo abrir {PUERTO}: {e}")
    print("La escena continuara funcionando con los valores iniciales.")

# ============================================================
# LOOP PRINCIPAL
# ============================================================

contador_frames = 0

while True:
    rate(60)
    
    if ser is not None:
        linea_valida = None
        
        while ser.in_waiting:
            try:
                linea_valida = ser.readline().decode(errors="ignore").strip()
            except Exception:
                break
        
        if linea_valida:
            try:
                valores_str = linea_valida.split(",")
                
                if len(valores_str) < 6:
                    raise ValueError(f"Se esperaban 6 valores, llegaron {len(valores_str)}")
                
                valores = [float(v.strip()) for v in valores_str[:6]]
                
                t1, t2, t3 = valores[0], valores[1], valores[2]
                r, p, y = valores[3], valores[4], valores[5]
                
                theta1, theta2, theta3 = t1, t2, t3
                roll, pitch, yaw = r, p, y
                
                theta1_f = ALPHA * t1 + (1 - ALPHA) * theta1_f
                theta2_f = ALPHA * t2 + (1 - ALPHA) * theta2_f
                theta3_f = ALPHA * t3 + (1 - ALPHA) * theta3_f
                
                roll_f = ALPHA * r + (1 - ALPHA) * roll_f
                pitch_f = ALPHA * p + (1 - ALPHA) * pitch_f
                yaw_f = ALPHA * y + (1 - ALPHA) * yaw_f
                
                actualizar_escena()
                
                contador_frames += 1
                
            except ValueError as e:
                print(f"Dato invalido: {e}")