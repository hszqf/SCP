#!/usr/bin/env python3
"""
Generate comprehensive SCP Foundation-style narratives for all events and options.
"""

import json
import pandas as pd
import random

# Load events with effects analysis
with open('/tmp/full_events_with_effects.json', 'r', encoding='utf-8') as f:
    events_data = json.load(f)

# SCP-style vocabulary pools
TITLES = {
    'monitoring': ['能量波动记录', '监测异常报告', '观测数据异常', '传感器警报', '检测系统警告', '扫描结果异常'],
    'containment': ['收容方案评估', '隔离协议更新', '封锁策略调整', '处置方案修订', '管制措施升级', '安保协议变更'],
    'incident': ['突发事件记录', '异常事故报告', '紧急情况汇报', '意外情况记录', '应急事件档案', '特殊事件登记'],
    'research': ['实验记录更新', '测试数据异常', '研究进展报告', '分析结果汇总', '样本观察记录', '试验流程变化'],
    'personnel': ['人员状况报告', '工作人员异常', 'D级人员记录', '员工安全事件', '人事变动通知', '团队状态评估'],
    'equipment': ['设备维护需求', '仪器故障报告', '系统运行异常', '装置性能下降', '设施损耗记录', '器材更换申请'],
    'crisis': ['紧急应对决策', '危机处理方案', '临界状态警报', '威胁等级上升', '风险评估报告', '应急预案启动'],
    'routine': ['日常维护记录', '例行检查汇报', '定期评估结果', '常规巡检报告', '周期检测数据', '标准流程执行']
}

EQUIPMENT = ['斯克兰顿现实稳定锚', '休谟场发生器', '认知过滤装置', 'Xyank时序调节器', '特斯拉防护网', 
             '电磁屏蔽系统', '生物遏制单元', '负熵泵', '膜拜抑制器', '模因免疫药剂', '反物质反应炉',
             '量子纠缠探测器', '精神力场隔离罩', '异维空间封锁器', '灵能抑制项圈']

PHENOMENA = ['能量峰值', '时空扭曲', '现实崩解', '维度裂隙', '因果紊乱', '熵增加速', '认知污染',
             '模因扩散', '物理常数偏移', '时间流速异常', '空间折叠', '物质相变', '信息辐射',
             '概率场扰动', '意识渗透', '记忆篡改', '感知扭曲', '实体化现象']

ACTIONS = {
    'aggressive': ['强行推进', '紧急封锁', '立即隔离', '强制镇压', '迅速封控', '直接遏制', '果断阻断', '紧急切断'],
    'cautious': ['谨慎观察', '暂缓执行', '审慎评估', '延后决策', '保守应对', '稳健处理', '缓步推进', '小心维持'],
    'resource': ['投入资源', '部署设备', '增派人员', '配置器材', '调拨物资', '分配预算', '启用储备', '追加经费'],
    'research': ['深入研究', '采集样本', '进行测试', '开展实验', '记录数据', '分析特性', '观测效应', '检验假说'],
    'diplomatic': ['上报总部', '请求支援', '申请调度', '协调资源', '联络外部', '寻求协助', '汇报情况', '请示决策'],
    'economic': ['出售数据', '回收产物', '变现样本', '转让成果', '交易情报', '处置废料', '清理库存', '资产转移'],
    'suppression': ['掩盖真相', '消除记忆', '篡改记录', '散布误导', '实施遮蔽', '执行清洗', '进行干预', '控制信息']
}

def analyze_effects(operations):
    """Analyze effect operations to determine narrative direction"""
    effects = {
        'progress_gain': 0,
        'progress_loss': 0,
        'money_gain': 0,
        'money_loss': 0,
        'panic_gain': 0,
        'panic_loss': 0,
        'entropy_gain': 0,
        'entropy_loss': 0,
        'pop_gain': 0,
        'pop_loss': 0
    }
    
    for op in operations:
        stat = op.get('statKey', '')
        value = op.get('value', 0)
        op_type = op.get('op', 'Add')
        
        if op_type == 'Add':
            if stat == 'progress':
                if value > 0:
                    effects['progress_gain'] += value
                else:
                    effects['progress_loss'] += abs(value)
            elif stat == 'money':
                if value > 0:
                    effects['money_gain'] += value
                else:
                    effects['money_loss'] += abs(value)
            elif stat in ['panic', 'worldPanic']:
                if value > 0:
                    effects['panic_gain'] += value
                else:
                    effects['panic_loss'] += abs(value)
            elif stat == 'negEntropy':
                if value > 0:
                    effects['entropy_gain'] += value
                else:
                    effects['entropy_loss'] += abs(value)
            elif stat == 'population':
                if value > 0:
                    effects['pop_gain'] += value
                else:
                    effects['pop_loss'] += abs(value)
    
    return effects

def generate_event_title(event_id, anomaly_name, effects_summary):
    """Generate event title based on context"""
    # Determine category based on event pattern
    event_num = int(event_id.split('_')[1])
    
    categories = ['monitoring', 'containment', 'incident', 'research', 'personnel', 
                  'equipment', 'crisis', 'routine']
    
    # Deterministic selection based on event ID
    category = categories[event_num % len(categories)]
    title_base = TITLES[category][event_num % len(TITLES[category])]
    
    # Limit to 12 characters
    if len(anomaly_name + title_base) <= 12:
        return f"{anomaly_name}{title_base}"
    else:
        return title_base[:12]

def generate_event_desc(event_id, anomaly_name, anomaly_id, effects_summary):
    """Generate event description with specific details"""
    details = []
    event_num = int(event_id.split('_')[1])
    
    # Opening context
    contexts = [
        f"站点监测系统记录到异常编号{anomaly_id}",
        f"收容单元内{anomaly_name}",
        f"针对{anomaly_id}的收容区域",
        f"研究员报告{anomaly_name}",
        f"{anomaly_name}收容室",
        f"对{anomaly_id}进行的第{event_num + 30}次观测中"
    ]
    
    opening = contexts[event_num % len(contexts)]
    
    # Specific details
    specific_details = [
        f"出现{random.randint(2, 8)}次{random.choice(PHENOMENA)}",
        f"检测到{random.choice(PHENOMENA)}，持续{random.randint(5, 120)}秒",
        f"的休谟指数下降至基准值的{random.randint(60, 95)}%",
        f"表现出非预期的{random.choice(PHENOMENA)}特征",
        f"周围{random.randint(1, 5)}名D级人员出现{random.choice(['幻视', '记忆错乱', '认知障碍', '生理异常', '精神污染'])}症状",
        f"导致{random.choice(EQUIPMENT)}负荷达到{random.randint(75, 98)}%",
        f"引发的{random.choice(PHENOMENA)}范围扩大了{random.uniform(0.5, 5):.1f}平方米",
        f"使{random.randint(2, 12)}件标准设备出现异常",
        f"触发了{random.randint(2, 7)}处独立监测点的警报系统",
        f"造成局部区域{random.choice(['时间流速差异', '空间扭曲', '物理常数偏移', '因果链断裂'])}",
        f"的{random.choice(['能量输出', '异常指数', '危险等级', '扩散速度'])}增加{random.randint(15, 85)}%",
        f"生成了{random.randint(3, 15)}份异常数据记录"
    ]
    
    detail = specific_details[event_num % len(specific_details)]
    
    # Consequence/requirement
    consequences = [
        "收容协议要求立即响应",
        "需要紧急决策干预",
        "技术部门提交了多套应对方案",
        "要求主管裁决处置方式",
        "触发了应急预案评估流程",
        "需要权衡资源投入与风险",
        "伦理委员会要求审核处理方案",
        "现有收容措施需要调整",
        "安保部门请求指示",
        "研究小组建议采取行动"
    ]
    
    consequence = consequences[event_num % len(consequences)]
    
    desc = f"{opening}{detail}。{consequence}。"
    
    # Truncate to fit length requirement (40-80 chars)
    if len(desc) > 80:
        desc = desc[:77] + "。"
    
    # Add redaction sometimes
    if event_num % 7 == 0:
        desc = desc.replace("数据", "[数据删除]", 1)
    elif event_num % 11 == 0:
        desc = desc.replace("记录", "[已编辑]", 1)
    
    return desc

def generate_option_text(option_id, effects):
    """Generate option text based on effects"""
    option_num = int(option_id.split('_')[-1].replace('OPT', ''))
    
    # Determine action type based on effects
    if effects['progress_gain'] > 0 and effects['money_loss'] > 0:
        action_pool = ACTIONS['resource']
    elif effects['progress_gain'] > 0 and effects['panic_gain'] > 0:
        action_pool = ACTIONS['aggressive']
    elif effects['money_gain'] > 0:
        action_pool = ACTIONS['economic']
    elif effects['progress_loss'] > 0 or effects['panic_loss'] > 0:
        action_pool = ACTIONS['cautious']
    elif effects['entropy_gain'] > 0:
        action_pool = ACTIONS['research']
    else:
        action_pool = ACTIONS['diplomatic']
    
    text = action_pool[option_num % len(action_pool)]
    
    return text[:10]  # Limit to 10 characters

def generate_result_text(option_id, effects):
    """Generate result text based on effects"""
    parts = []
    
    # Progress effects
    if effects['progress_gain'] > 0:
        progress_outcomes = [
            f"任务推进{int(effects['progress_gain']*100)}%",
            f"收容进度加快{int(effects['progress_gain']*100)}%",
            f"处置效率提升{int(effects['progress_gain']*100)}%",
            f"工作完成度增加{int(effects['progress_gain']*100)}%"
        ]
        parts.append(random.choice(progress_outcomes))
    elif effects['progress_loss'] > 0:
        parts.append(f"进度受阻，延后{int(effects['progress_loss']*100)}%")
    
    # Money effects
    if effects['money_loss'] > 0:
        parts.append(f"耗资{int(effects['money_loss'])}预算")
    elif effects['money_gain'] > 0:
        parts.append(f"回收{int(effects['money_gain'])}资金")
    
    # Panic effects
    if effects['panic_gain'] > 0:
        panic_outcomes = [
            f"公众恐慌度上升{int(effects['panic_gain'])}点",
            f"地区不稳定性增加",
            f"需启动信息控制协议",
            f"触发舆情监控机制"
        ]
        parts.append(random.choice(panic_outcomes))
    elif effects['panic_loss'] > 0:
        parts.append("成功稳定局势")
    
    # Entropy effects
    if effects['entropy_gain'] > 0:
        parts.append(f"负熵值增加{int(effects['entropy_gain'])}")
    elif effects['entropy_loss'] > 0:
        parts.append("系统熵值下降")
    
    # Population effects
    if effects['pop_loss'] > 0:
        parts.append(f"造成{int(effects['pop_loss'])}人伤亡")
    
    # Combine parts
    if not parts:
        result = "指令已执行，情况维持稳定"
    elif len(parts) == 1:
        result = parts[0]
    else:
        result = "，".join(parts[:2])  # Max 2 parts to fit length
    
    # Add finishing touch
    option_num = int(option_id.split('_')[-1].replace('OPT', ''))
    if option_num % 3 == 0:
        result += "，已归档"
    elif option_num % 3 == 1:
        result += "，记录完成"
    
    return result[:25]  # Limit to 25 characters

# Generate narratives for all events
print("Generating narratives for all events and options...")

event_narratives = {}
option_narratives = {}

for event in events_data:
    event_id = event['eventDefId']
    anomaly = event.get('anomaly', {})
    anomaly_name = anomaly.get('name', '未知异常')
    anomaly_id = anomaly.get('id', 'AN-XXX')
    
    # Analyze all options for this event to understand overall effects
    all_effects = []
    for opt in event['options']:
        effects = analyze_effects(opt['operations'])
        all_effects.append(effects)
    
    # Generate event title and desc
    title = generate_event_title(event_id, anomaly_name, all_effects)
    desc = generate_event_desc(event_id, anomaly_name, anomaly_id, all_effects)
    
    event_narratives[event_id] = {
        'title': title,
        'desc': desc
    }
    
    # Generate option narratives
    for opt in event['options']:
        option_id = opt['optionId']
        effects = analyze_effects(opt['operations'])
        
        text = generate_option_text(option_id, effects)
        result_text = generate_result_text(option_id, effects)
        
        option_narratives[option_id] = {
            'text': text,
            'resultText': result_text
        }

print(f"Generated {len(event_narratives)} event narratives")
print(f"Generated {len(option_narratives)} option narratives")

# Load Excel file
print("\nLoading Excel file...")
xl_path = 'GameData/Local/game_data.xlsx'
xl = pd.ExcelFile(xl_path)
events_df = pd.read_excel(xl, 'Events')
options_df = pd.read_excel(xl, 'EventOptions')

print(f"Events sheet: {len(events_df)} rows")
print(f"EventOptions sheet: {len(options_df)} rows")

# Update Events sheet
print("\nUpdating Events sheet...")
updated_count = 0
for idx, row in events_df.iterrows():
    if idx < 2:  # Skip header rows
        continue
    
    event_id = row['eventDefId']
    if event_id in event_narratives:
        events_df.at[idx, 'title'] = event_narratives[event_id]['title']
        events_df.at[idx, 'desc'] = event_narratives[event_id]['desc']
        updated_count += 1

print(f"Updated {updated_count} events")

# Update EventOptions sheet
print("\nUpdating EventOptions sheet...")
updated_count = 0
for idx, row in options_df.iterrows():
    option_id = row['optionId']
    if pd.notna(option_id) and option_id in option_narratives:
        options_df.at[idx, 'text'] = option_narratives[option_id]['text']
        options_df.at[idx, 'resultText'] = option_narratives[option_id]['resultText']
        updated_count += 1

print(f"Updated {updated_count} options")

# Save back to Excel
print("\nSaving updated Excel file...")
with pd.ExcelWriter(xl_path, engine='openpyxl', mode='a', if_sheet_exists='replace') as writer:
    events_df.to_excel(writer, sheet_name='Events', index=False)
    options_df.to_excel(writer, sheet_name='EventOptions', index=False)

print("✓ Excel file updated successfully!")

# Show samples
print("\n=== Sample Event ===")
sample_event = events_df[events_df['eventDefId'] == 'EV_001'].iloc[0]
print(f"ID: {sample_event['eventDefId']}")
print(f"Title: {sample_event['title']}")
print(f"Desc: {sample_event['desc']}")

print("\n=== Sample Options ===")
sample_opts = options_df[options_df['eventDefId'] == 'EV_001']
for _, opt in sample_opts.iterrows():
    print(f"ID: {opt['optionId']}")
    print(f"Text: {opt['text']}")
    print(f"Result: {opt['resultText']}")
    print()
