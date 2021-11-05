#!/sbin/openrc-run
# created using "startup" by JanD https://github.com/Jan0660/JanD

description="The Jan Process Manager"

# environment variables

PATH={0}
export PATH
JAND_HOME={1}
export JAND_HOME
JAND_PIPE={2}
export JAND_PIPE

pidfile=/run/$RC_SVCNAME.pid

source /lib/rc/sh/functions.sh

start() {{
    ebegin "Starting JanD@{4}"
    start-stop-daemon --start \
        --pidfile $pidfile \
        --make-pidfile \
        --background \
        --user {4} \
        --exec "{3}" \
        -- \
        start-daemon
    eend $?
}}

stop() {{
    ebegin "Stopping JanD@{4}"
    "{3}" kill > /dev/null
    eend $?
    rm $pidfile
}}
