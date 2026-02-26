#!/bin/sh

mkfifo stream_pipe

# test with -k av1_hdr later

gpu-screen-recorder -w screen -k h264 -c mkv -f 30 -keyint 0.1 -o stream_pipe

rm stream_pipe