# 무장 데이터 추가 작업 안내 (하위 작업용)

## 기존 무장 ID (중복 금지, 혈연 참조 가능)
dong_zhuo(동탁), lu_bu(여포), li_ru(이유), li_jue(이각), guo_si(곽사), hua_xiong(화웅), xu_rong(서영), zhang_ji(장제), jia_xu(가후), zhang_liao(장료), gao_shun(고순), zhang_xiu(장수), wang_yun(왕윤), niu_fu(우보), yuan_shao(원소), yan_liang(안량), wen_chou(문추), tian_feng(전풍), feng_ji(봉기), guo_tu(곽도), xu_you(허유), xun_chen(순심), gao_lan(고람), han_fu(한복), zhang_he(장합), shen_pei(심배), ju_shou(저수), qu_yi(국의), cao_cao(조조), xiahou_dun(하후돈), xiahou_yuan(하후연), cao_ren(조인), cao_hong(조홍), yue_jin(악진), li_dian(이전), chen_gong(진궁), dian_wei(전위), sun_jian(손견), cheng_pu(정보), huang_gai(황개), han_dang(한당), zu_mao(조무), sun_ce(손책), sun_quan(손권), zhou_yu(주유), lu_su(노숙), yuan_shu(원술), ji_ling(기령), zhang_xun(장훈), qiao_rui(교유), yan_xiang(염상), lei_bo(뇌박), gongsun_zan(공손찬), gongsun_yue(공손월), yan_gang(엄강), tian_kai(전해), guan_jing(관정), zhao_yun(조운), liu_bei(유비), guan_yu(관우), zhang_fei(장비), jian_yong(간옹), liu_biao(유표), kuai_liang(괴량), kuai_yue(괴월), cai_mao(채모), huang_zu(황조), wen_pin(문빙), zhang_yun(장윤), huang_zhong(황충), wei_yan(위연), liu_yan(유언), liu_zhang(유장), zhang_ren(장임), yan_yan(엄안), huang_quan(황권), zhang_song(장송), zhang_lu(장로), wu_yi(오의), ma_teng(마등), han_sui(한수), ma_chao(마초), pang_de(방덕), ma_dai(마대), tao_qian(도겸), mi_zhu(미축), mi_fang(미방), chen_deng(진등), chen_gui(진규), cao_bao(조표), kong_rong(공융), taishi_ci(태사자), gongsun_du(공손도), gongsun_kang(공손강), xun_yu(순욱), guo_jia(곽가), cheng_yu(정욱), xu_huang(서황), zhuge_liang(제갈량), pang_tong(방통)

## 특기 ID (traits에는 이 중에서만)
- breakthrough 돌파술: 어린 진형 공격 +15%
- wing_master 익전: 학익 진형 포위 보너스 2배
- iron_wall 철벽: 방원 진형 방어 +15%
- serpent 장진: 장사 진형 반격 피해 +20%
- cavalry 기병술: 추행 진형 공격 +15%
- marksman 신궁: 안행 진형 공격 +15%
- siegecraft 공성술: 거점·시설 피해 +25%
- naval 수전: 수진 공격·방어 +15%
- valiant 맹장: 모든 진형 공격 +10%
- chain_master 연환: 전법 연쇄 피해 보너스 +10%p
- vanguard 선봉: 출진 후 2턴 동안 공격 +20%
- indomitable 불굴: 병력 30% 이하일 때 방어 +20%
- inspire 고무: 사기 감소량 절반
- intimidate 위압: 인접한 적 부대 사기 매 턴 -5
- ambush_master 기습: 매복 효과 +50%, 숲·안개에서 공격 +10%
- mountain 산악전: 산 지형 보정 페널티 무시
- forced_march 급행: 이동력 +1
- peerless 일기당천: 일기토 특수 카드, 일기토 기세 +10%
- fire_master 화공: 화계 성공률 +20%, 화재 피해 +50%
- insight 간파: 적 계략 간파율 상승
- confuser 교란술: 교란 성공률 +20%
- strategist 신산: 전법 성공률 +10%
- schemer 귀모: 전략 계략(이간·매수·내응) 성공률 +20%
- astronomy 천문: 2턴 뒤 날씨를 미리 안다
- agriculture 농정: 태수일 때 도시 수확 +15%
- commerce 상재: 태수일 때 도시 금 수입 +15%
- builder 축성: 건설 기간 -1턴, 성벽·성문 보수 효율 증가
- benevolence 인덕: 태수일 때 도시 민심 매월 +2
- recruiter 징모: 징병량 +20%, 징병 민심 감소 절반
- drill 조련: 훈련 효율 +50%
- craftsman 장인: 병장기 생산 +20%
- erudite 해박: 기교 포인트 획득 +20%
- eloquence 언변: 설전 특수 카드, 외교 성공률 +10%
- fame 명성: 등용 성공률 +20%
- talent_eye 인재안: 탐색 시 재야 무장 발견 확률 증가
- dignity 위엄: 군주일 때 세력 무장의 충성 하락 완화

## 진형 ID (aptitude 키, 모두 필수)
fish_scale 어린, crane_wing 학익, square 방원, long_snake 장사, wedge 추행(기병), wild_geese 안행(궁노), siege 공성진, naval 수진

## 숨은 값 상성(compat, 0~149, 원형) 기준 — 군주와 가까울수록 충성·등용이 쉽다
조조·위 계열 20~35, 유비·촉 계열 70~80, 손씨·오 계열 45~55, 원소·원씨 계열 95~105, 동탁·서량 군벌 130~142, 유표 형주 84~87, 유언·유장 익주 87~90, 마등·서량 58~62, 공손씨(요동) 8~12, 공손찬 68~72, 남만 110~120, 황건 125~128, 한 조정(하진·노식 등) 65~70, 기타 군웅은 자유

## 형식 (data/base/officers.json의 한 항목과 똑같이)
{
  "id": "lu_bu",
  "name": {
    "ko": "여포",
    "zh": "呂布"
  },
  "gender": "male",
  "birth": 156,
  "death": 198,
  "stats": {
    "ldr": 88,
    "war": 100,
    "int": 30,
    "pol": 18,
    "cha": 40
  },
  "hidden": {
    "loyalty": 1,
    "ambition": 15,
    "compat": 138
  },
  "aptitude": {
    "fish_scale": "A",
    "crane_wing": "B",
    "square": "C",
    "long_snake": "B",
    "wedge": "S",
    "wild_geese": "A",
    "siege": "C",
    "naval": "C"
  },
  "traits": [
    "peerless",
    "cavalry"
  ],
  "ruler_personality": {
    "aggression": 9,
    "faith": 1,
    "domestic": 2,
    "diplomacy": 2
  }
}

## 규칙
- stats 5종 1~100. 이 게임만의 수치를 직접 판단해서 매긴다. 다른 게임(코에이 삼국지 등)의 수치를 그대로 옮기지 말 것. 인물의 역사적 평가에 맞게 상대적 서열이 자연스러워야 한다. 90 이상은 정말 뛰어난 인물에게만.
- hidden: loyalty(의리) 1~15, ambition(야망) 1~15, compat(상성) 위 기준.
- aptitude: 8개 진형 모두 S/A/B/C. 통솔 50 이상이면 C가 아닌 진형이 하나 이상. 수진 S/A는 오·형주·강남 출신 위주, 추행 S는 서량·북방 기병장 위주.
- traits: 보통 무장 1개, 이름난 무장 2개. 위 목록의 ID만.
- birth: 출생년(모르면 역사적 활동기로 추정), death: 사망년(모르면 null).
- gender: male/female.
- kin: father/siblings/spouse는 기존 ID나 같은 묶음 안의 ID만 참조. 확실하지 않으면 생략.
- ruler_personality: 시나리오에서 군주가 될 수 있는 인물에게만 (aggression, faith, domestic, diplomacy 각 1~10).
- id는 영문 소문자+밑줄 (병음 기반), 기존 ID와 다른 묶음 ID와 겹치지 않게 지정된 ID를 그대로 쓴다.