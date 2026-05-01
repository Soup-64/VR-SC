extends MeshInstance3D

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	pass

var marker: XRNode3D
var offset: Vector3 = Vector3.ZERO
var offScale: float = 0.03

var Lvec: Vector2
var Rvec: Vector2

var target: Vector3

func _on_manager_added_spatial_entity(node: XRNode3D) -> void:
	print("got new entity! ", node)
	print("\tnode is at ", node.position, " and contains data ", node.marker_tracker.get_marker_data())
	print("\tnode rotation: ", node.rotation)
	marker = node
	self.position = node.position
	self.rotation = node.rotation

func _process(_delta: float) -> void:
	if marker == null:
		#print("no marker!")
		return
	offset.x += Lvec.x * offScale
	offset.y += Lvec.y * offScale
	offset.z += Rvec.y * offScale
	#print("offset! ", offset)
	target = marker.position + offset
	self.position = lerp(self.position, target, 0.1)
	#self.rotation = marker.rotation
	# copying the rotation constantly results in some ugly jitter

#adjust monitor position
func _on_left_hand_input_vector_2_changed(_btnName: String, value: Vector2) -> void:
	#print("L joy! pos ", value)
	Lvec = value

#doesn't work apparently...
func _on_manager_removed_spatial_entity(node: XRNode3D) -> void:
	print("Lost entity! ", node)

func _on_right_hand_input_vector_2_changed(_name: String, value: Vector2) -> void:
	#print("R joy! pos ", value)
	Rvec = value
