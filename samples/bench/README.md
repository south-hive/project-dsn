# 여러 앱 Source

telemetry-app, test-app, orchestrator를 서로 다른 프로세스로 실행해 같은 시험 PC의 데이터를 발행한다. 실물 장치·드라이버에 접근하지 않는다.

```sh
# 루트, 터미널 1: 필터 없는 기본 파이프라인
make bench-host DSN_BUILD_JOBS=1
# 터미널 2
make bench-sources
```

`http://127.0.0.1:7071/`에서 연결 → bench → 처음 조회를 선택한다. 세 Python 앱이 총 30개의 합성 이벤트를 보내고 SQLite에 원본/결과를 저장한다. 필터 예제를 사용하려면 첫 명령을 `make pipeline-host DSN_BUILD_JOBS=1`로 바꾼다. [처리 규칙](../pipeline/README.md).

```sh
PYTHONPATH=sdk/source/python/src python samples/bench/source.py --role test-app --pc bench-02 --dut dut-08 --run run-17
```

각 앱은 고유한 source_id/instance_id를 쓰고 PC·DUT·시험 실행 식별자를 공유한다. instance_id는 프로세스 재시작마다 바뀐다. 실제 DUT 식별자는 재사용 슬롯 번호와 구분한다. PC 전체 이벤트의 dut_id는 `pc-wide`처럼 명시할 수 있다.

예제 payload:

```json
{"schema":"bench.v1","pc_id":"bench-01","dut_id":"dut-01","run_id":"run-17","instance_id":"app-start-3","role":"telemetry-app","sequence":0,"values":{"read_iops":12000,"latency_us":91,"read_errors":0}}
```

공통 식별 field·sequence·scalar values를 가진 예제 전용 규격이다. values는 value_ 접두 field가 된다. 단위·구간·샘플링 의미는 Source/Workspace 규격으로 합의한다. 다른 JSON·binary payload는 다른 Workspace에서 해석하면 된다.

사무 PC는 시험 PC의 웹에 접속한다. 원격 bind에는 사용자 토큰/권한과 HTTPS 배치가 필요하며, ingressBind 기본 loopback은 유지할 수 있다. 중앙 서버가 있으면 같은 Host를 배치하고 Source의 host/port를 중앙 주소로 설정한다. 중앙 직접 수집과 로컬 수집 사이의 자동 동기화는 아직 제공하지 않는다.

실제 앱 Source 연동은 SDK가 담당한다. 드라이버 연동·검증은 DSN 작업 범위에서 제외한다.
