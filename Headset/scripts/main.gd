extends Node3D

var xr_interface: XRInterface

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
