extends XRAnchor3D

var marker_tracker: OpenXRMarkerTracker

func _ready() -> void:
	marker_tracker = XRServer.get_tracker(tracker)
	if marker_tracker:
		match marker_tracker.marker_type:
			OpenXRSpatialComponentMarkerList.MARKER_TYPE_QRCODE:
				var data: Variant = marker_tracker.get_marker_data()
				print("qr code!")
				if data is String:
					# Data is a QR code as a string, usually a URL.
					
					pass
				elif data is PackedByteArray:
					# Data is binary, can be anything.
					pass
			OpenXRSpatialComponentMarkerList.MARKER_TYPE_MICRO_QRCODE:
				var data: Variant = marker_tracker.get_marker_data()
				if data is String:
					# Data is a QR code as a string, usually a URL.
					pass
				elif data is PackedByteArray:
					# Data is binary, can be anything.
					pass
			OpenXRSpatialComponentMarkerList.MARKER_TYPE_ARUCO:
				# Use marker_tracker.marker_id to identify the marker.
				pass
			OpenXRSpatialComponentMarkerList.MARKER_TYPE_APRIL_TAG:
				# Use marker_tracker.marker_id to identify the marker.
				pass