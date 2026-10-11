extends Label
## Debug overlay: fps, frame ms, tick ms, entity counts and tick allocations —
## roadmap M0-18.
##
## A three-finger tap toggles it. It reads the host a few times a second and
## only while visible, so it adds nothing to a frame it is not drawn in. Release
## builds free it at boot: the numbers are for development, and a stray three-
## finger tap in a shipped game would put them in front of a player.

## How often the text refreshes, s. Readability, not a tunable of the game.
const REFRESH_INTERVAL: float = 0.25
const FINGERS_TO_TOGGLE: int = 3

@export var host: Node2D

var _touches: Dictionary = {}
var _elapsed: float = 0.0
var _frames: int = 0


func _ready() -> void:
	if not OS.is_debug_build():
		queue_free()
		return
	hide()


func _input(event: InputEvent) -> void:
	if not event is InputEventScreenTouch:
		return
	var touch: InputEventScreenTouch = event
	if touch.pressed:
		_touches[touch.index] = true
		# Toggle as the third finger lands, so lifting them does not undo it.
		if _touches.size() == FINGERS_TO_TOGGLE:
			_toggle()
	else:
		_touches.erase(touch.index)


func _process(delta: float) -> void:
	if not visible:
		return
	_elapsed += delta
	_frames += 1
	if _elapsed < REFRESH_INTERVAL:
		return
	text = "fps    %d\nframe  %.2f ms\ntick   %.3f ms  (peak %.3f)\nsnakes %d live\nalloc  %d B last tick, %d B all ticks" % [
		Engine.get_frames_per_second(),
		_elapsed * 1000.0 / _frames,
		host.LastTickMs,
		host.PeakTickMs,
		host.LiveSnakeCount,
		host.LastTickAllocatedBytes,
		host.TickAllocatedBytes,
	]
	host.ResetPeakTickMs()
	_elapsed = 0.0
	_frames = 0


func _toggle() -> void:
	visible = not visible
	_elapsed = 0.0
	_frames = 0
	if visible:
		host.ResetPeakTickMs()
		text = "…"
