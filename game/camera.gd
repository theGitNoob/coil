extends Camera2D
## Damped follow, look-ahead and radius-driven zoom for the focus snake — spec §7.4 (M0-15).
##
## GDScript because camera glue is presentation (CLAUDE.md language split). It
## reads three focus values from the host once per frame (head, heading,
## radius) — part of ARCH §3 crossing #2, never per entity. Those values are
## interpolated on demand from the engine's physics fraction, so this node's
## order relative to SnakeRendererNode does not matter.
##
## Camera2D's own position smoothing stays off: it is not critically damped,
## and §7.4 asks for a specific smooth time.

const BALANCE_PATH: String = "res://data/balance.tres"

@export var host: Node2D

var _smooth_time: float = 0.0
var _look_ahead: float = 0.0
var _zoom_base: float = 0.0
var _zoom_exp: float = 0.0
var _zoom_min: float = 0.0
var _zoom_max: float = 0.0
var _zoom_smooth_time: float = 0.0
var _radius_base: float = 0.0

var _position_state: Damped = Damped.new()
# Zoom is a scalar; it rides in the x component so one damping routine serves both.
var _zoom_state: Damped = Damped.new()
var _primed: bool = false


## Critically damped spring, closed form (Game Programming Gems 4, ch. 1.10).
## Stable at any frame time, unlike a per-frame lerp factor.
class Damped:
	var value: Vector2 = Vector2.ZERO
	var velocity: Vector2 = Vector2.ZERO

	func step(target: Vector2, smooth_time: float, dt: float) -> void:
		var omega: float = 2.0 / smooth_time
		var x: float = omega * dt
		var k: float = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x)
		var change: Vector2 = value - target
		var temp: Vector2 = (velocity + omega * change) * dt
		velocity = (velocity - omega * temp) * k
		value = target + (change + temp) * k


func _ready() -> void:
	var balance: Resource = load(BALANCE_PATH)
	if balance == null:
		push_error("camera: could not load %s" % BALANCE_PATH)
		set_process(false)
		return
	_smooth_time = balance.CameraSmoothTime
	_look_ahead = balance.CameraLookAhead
	_zoom_base = balance.CameraZoomBase
	_zoom_exp = balance.CameraZoomExp
	_zoom_min = balance.CameraZoomMin
	_zoom_max = balance.CameraZoomMax
	_zoom_smooth_time = balance.CameraZoomSmoothTime
	_radius_base = balance.RadiusBase


func _process(delta: float) -> void:
	var head: Vector2 = host.FocusPosition
	var heading: float = host.FocusHeading
	var radius: float = host.FocusRadius

	var target: Vector2 = head + Vector2.from_angle(heading) * _look_ahead
	var zoom_target: float = clampf(
		_zoom_base * pow(radius / _radius_base, _zoom_exp), _zoom_min, _zoom_max)

	if not _primed:
		# Snap on the first frame so the view does not swoop in from the origin.
		_position_state.value = target
		_zoom_state.value = Vector2(zoom_target, 0.0)
		_primed = true
	else:
		_position_state.step(target, _smooth_time, delta)
		_zoom_state.step(Vector2(zoom_target, 0.0), _zoom_smooth_time, delta)

	position = _position_state.value
	zoom = Vector2(_zoom_state.value.x, _zoom_state.value.x)
