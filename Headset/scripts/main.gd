extends Node3D

var xr_interface: XRInterface
@onready var world_environment: WorldEnvironment = $WorldEnvironment

func _input(event: InputEvent) -> void:
	print("input! ", event)
	if event.is_action_pressed("ui_accept"):
		$Plane.position.y += 0.01

func _ready() -> void:
	xr_interface = XRServer.find_interface("OpenXR")
	if xr_interface and xr_interface.is_initialized():
		print("OpenXR initialized successfully")

		# Turn off v-sync!
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
		# Change our main viewport to output to the HMD
		var viewport:Viewport = get_viewport()
		viewport.use_xr = true
		viewport.transparent_bg = true #required alongside XR_ENV_BLEND_MODE_ALPHA_BLEND
	else:
		print("OpenXR not initialized, please check if your headset is connected")

func _enable_passthrough(enable: bool) -> void:
	print("passing!")
	var openxr_interface: OpenXRInterface = XRServer.find_interface("OpenXR")

	# Enable passthrough if true and XR_ENV_BLEND_MODE_ALPHA_BLEND is supported.
	# Otherwise, set environment to non-passthrough settings.
	if enable and openxr_interface.get_supported_environment_blend_modes().has(XRInterface.XR_ENV_BLEND_MODE_ALPHA_BLEND):
		get_viewport().transparent_bg = true
		world_environment.environment.background_mode = Environment.BG_COLOR
		world_environment.environment.background_color = Color(0.0, 0.0, 0.0, 0.0)
		openxr_interface.environment_blend_mode = XRInterface.XR_ENV_BLEND_MODE_ALPHA_BLEND
	else:
		get_viewport().transparent_bg = false
		world_environment.environment.background_mode = Environment.BG_SKY
		openxr_interface.environment_blend_mode = XRInterface.XR_ENV_BLEND_MODE_OPAQUE
