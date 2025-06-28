-- ������� ������� ��� �������� �����������
CREATE OR REPLACE FUNCTION notify_report_change()
RETURNS TRIGGER AS $$
BEGIN
    -- ���������� ����������� ������ � ��������������� ������
    PERFORM pg_notify('report_change', '������� ������ �� ������� Rep_REPORTS');
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- ������� ������� ��� �������, ���������� � �������� �������
CREATE TRIGGER report_change_trigger
AFTER INSERT OR UPDATE OR DELETE ON "Rep_REPORTS"
FOR EACH ROW EXECUTE FUNCTION notify_report_change();