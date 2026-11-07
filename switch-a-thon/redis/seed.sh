#!/bin/sh
set -eu

redis-cli -h "$REDIS_HOST" SET demo:resolveiq:seed:version "1"
redis-cli -h "$REDIS_HOST" SET demo:resolveiq:sample:zone-lookup "carrierServiceType=GROUND;zone=US-WEST-TO-NORTHEAST"
redis-cli -h "$REDIS_HOST" SET demo:resolveiq:sample:redis-timeout "memory_used_percent=91;threshold=85"
echo "ResolveIQ demo keys seeded in Redis."
