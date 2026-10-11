extends Node
## Player input -> the player's InputCommand, once per tick — ARCH §3 crossing #1,
## D-04 (M0-17).
##
## The only place touch input becomes a command. It runs its physics step
## before ArenaHostNode's (process_physics_priority -1 in arena.tscn), so the
## command written here is the one the tick that follows consumes. Boost and
## cast are always false until their buttons exist (M1-11, M3-08).

enum ControlScheme { JOYSTICK, FINGER_FOLLOW }

## D-04: the floating joystick is the default, finger-follow the alternative.
## M4-14 persists the choice as a setting.
@export var control_scheme: ControlScheme = ControlScheme.JOYSTICK
@export var host: Node2D
@export var joystick: Control

var _finger_index: int = -1
var _finger: Vector2 = Vector2.ZERO
var _heading: float = 0.0
var _has_heading: bool = false


func _ready() -> void:
	if control_scheme == ControlScheme.FINGER_FOLLOW:
		# One steering input at a time: a stick appearing under a finger-follow
		# touch would read as a second, competing control.
		joystick.hide()
		joystick.process_mode = Node.PROCESS_MODE_DISABLED


func _input(event: InputEvent) -> void:
	if control_scheme != ControlScheme.FINGER_FOLLOW:
		return
	if event is InputEventScreenTouch:
		var touch: InputEventScreenTouch = event
		if touch.pressed and _finger_index == -1:
			_finger_index = touch.index
			_follow(touch.position)
		elif not touch.pressed and touch.index == _finger_index:
			_finger_index = -1
	elif event is InputEventScreenDrag:
		var drag: InputEventScreenDrag = event
		if drag.index == _finger_index:
			_follow(drag.position)


func _physics_process(_delta: float) -> void:
	if control_scheme == ControlScheme.JOYSTICK:
		_has_heading = joystick.has_heading
		_heading = joystick.heading
	# Until the player has pointed somewhere the slot keeps its spawn heading,
	# so there is nothing to send.
	if _has_heading:
		host.SetPlayerCommand(_heading, false, false)


## Finger-follow aims from the screen centre, where the camera keeps the head,
## and reuses the stick's dead zone so a finger resting on the centre does not
## spin the heading.
func _follow(position_in: Vector2) -> void:
	_finger = position_in
	var offset: Vector2 = _finger - get_viewport().get_visible_rect().size * 0.5
	if offset.length() >= joystick.dead_zone_radius:
		_heading = offset.angle()
		_has_heading = true
