extends TextureRect

# Video Configuration
var img_width:int = 1920
var img_height:int = 1080
var expected_bytes:int = img_width * img_height * 4 # RGBA

# Rendering Device Variables
var rd: RenderingDevice
var texture_rid: RID
var tex_rd: Texture2DRD

# Shared Memory Variable (Assuming the ElSuicio class is named SharedMemory)
var shm:SharedMemory

func _ready() -> void:
	# 1. Setup the GPU Texture (Texture2DRD)
	rd = RenderingServer.get_rendering_device()
	var fmt := RDTextureFormat.new()
	fmt.width = img_width
	fmt.height = img_height
	fmt.format = RenderingDevice.DATA_FORMAT_R8G8B8A8_UNORM
	fmt.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT | RenderingDevice.TEXTURE_USAGE_CAN_UPDATE_BIT
	
	# Initialize with empty bytes
	var empty_data:PackedByteArray = PackedByteArray()
	empty_data.resize(expected_bytes)
	texture_rid = rd.texture_create(fmt, RDTextureView.new(), [empty_data])
	
	tex_rd = Texture2DRD.new()
	tex_rd.texture_rd_rid = texture_rid
	texture = tex_rd

	# 2. Setup Shared Memory Connection
	shm = SharedMemory.new()
	# Connect to the exact name/path defined in your GStreamer shmsink
	var success:Error = shm.open("godot_video_shm", expected_bytes) 
	
	if not success:
		push_error("Failed to connect to Shared Memory block!")

func _process(_delta: float) -> void:
	if not shm or shm.get_status() == shm.Status.STATUS_OPEN:
		return
	var raw_frame_data: PackedByteArray = shm.read(expected_bytes, 0)
	
	if raw_frame_data.size() == expected_bytes:
		rd.texture_update(texture_rid, 0, raw_frame_data)
