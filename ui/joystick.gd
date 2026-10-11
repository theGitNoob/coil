extends Control
## Floating virtual joystick — spec §3 (M0-16).
##
## GDScript because input glue is presentation (CLAUDE.md language split). It
## reads raw events in `_input` rather than through GUI focus, and tracks only
## the touch index it captured: steering, boost and ability are held at the
## same time, so every other finger must be left alone. It outputs a target
## heading, never a velocity; M0-17 feeds `heading` into the sim once per tick.

const BALANCE_PATH: String = "res://data/balance.tres"
const DP_BASELINE_DPI: float = 160.0  # Android's dp is 1/160 inch.

# Placeholder visuals; A-04 replaces them. Art, not tunables.
const RING_WIDTH: float = 3.0
const RING_ALPHA: float = 0.35
const KNOB_SCALE: float = 0.4
const KNOB_ALPHA: float = 0.6

## Direction the stick last pointed past the dead zone, radians. Only this
## script writes it. The camera never rotates, so a screen direction is a world
## direction.
var heading: float = 0.0
## False until the first drag past the dead zone; until then the player keeps
## its spawn heading. Only this script writes it.
var has_heading: bool = false

var _zone: float = 0.0
var _dead_zone: float = 0.0
var _drag_out: float = 0.0
var _fade_time: float = 0.0
var _radius: float = 0.0

var _touch_index: int = -1
var _anchor: Vector2 = Vector2.ZERO
var _finger: Vector2 = Vector2.ZERO
var _alpha: float = 0.0


func _ready() -> void:
	var balance: Resource = load(BALANCE_PATH)
	if balance == null:
		push_error("joystick: could not load %s" % BALANCE_PATH)
		set_process(false)
		set_process_input(false)
		return
	_zone = balance.JoystickZone
	_dead_zone = balance.JoystickDeadZone
	_drag_out = balance.JoystickDragOut
	_fade_time = balance.JoystickFadeTime

	var radius_dp: float = balance.JoystickRadiusDp
	var dpi: int = DisplayServer.screen_get_dpi()
	var stretch: float = get_viewport().get_screen_transform().get_scale().x
	if dpi <= 0 or stretch <= 0.0:
		push_warning("joystick: dpi %d / stretch %.3f unusable, treating 1 dp as 1 canvas unit" % [dpi, stretch])
		_radius = radius_dp
	else:
		_radius = radius_dp * float(dpi) / DP_BASELINE_DPI / stretch
	print("COIL joystick\nradius        %.1f canvas u (%d dp at %d dpi, stretch %.3f)\nzone          left %.0f%% of %.0f u" % [
		_radius, int(radius_dp), dpi, stretch, _zone * 100.0, size.x])


func _input(event: InputEvent) -> void:
	# Event positions are in the stretched 1280x720-based canvas space, which is
	# this full-rect Control's local space.
	if event is InputEventScreenTouch:
		var touch: InputEventScreenTouch = event
		if touch.pressed:
			_press(touch)
		elif touch.index == _touch_index:
			_touch_index = -1
	elif event is InputEventScreenDrag:
		var drag: InputEventScreenDrag = event
		if drag.index == _touch_index:
			_drag(drag.position)


func _process(delta: float) -> void:
	if _touch_index != -1 or _alpha <= 0.0:
		return
	_alpha = maxf(_alpha - delta / _fade_time, 0.0)
	queue_redraw()


func _draw() -> void:
	if _alpha <= 0.0:
		return
	draw_arc(_anchor, _radius, 0.0, TAU, 64, Color(1.0, 1.0, 1.0, RING_ALPHA * _alpha), RING_WIDTH, true)
	var knob: Vector2 = _anchor + (_finger - _anchor).limit_length(_radius)
	draw_circle(knob, KNOB_SCALE * _radius, Color(1.0, 1.0, 1.0, KNOB_ALPHA * _alpha))


func _press(touch: InputEventScreenTouch) -> void:
	# Ignored touches are not marked handled, so buttons still receive them.
	if _touch_index != -1 or touch.position.x >= _zone * size.x:
		return
	_touch_index = touch.index
	_anchor = touch.position
	_finger = touch.position
	_alpha = 1.0
	queue_redraw()


func _drag(position_in: Vector2) -> void:
	_finger = position_in
	var offset: Vector2 = _finger - _anchor
	var limit: float = _drag_out * _radius
	if offset.length() > limit:
		# Drag-out: the anchor trails the finger so a drifting thumb keeps control.
		_anchor = _finger - offset.normalized() * limit
		offset = _finger - _anchor
	if offset.length() >= _dead_zone * _radius:
		heading = offset.angle()
		has_heading = true
	queue_redraw()
