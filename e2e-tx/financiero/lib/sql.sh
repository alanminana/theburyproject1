#!/bin/bash
# Lectura/escritura acotada a BuryQA_FIN (contenedor bury-qa-fin-db). Uso: sql.sh "SELECT ..."
B64=$(printf '%s' "$1" | base64 -w0)
docker exec bury-qa-fin-db bash -c "export SQLCMDPASSWORD=\$MSSQL_SA_PASSWORD; echo $B64 | base64 -d | /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d BuryQA_FIN -s '|' -W -b -i /dev/stdin"
